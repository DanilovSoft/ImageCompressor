using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Diagnostics;
using DanilovSoft;
#if NETCOREAPP3_1
using System.Threading.Tasks.Dataflow;
#endif


namespace TplDataflowGui
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private DirectoryInfo GetDirectory()
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.SelectedPath = AppDomain.CurrentDomain.BaseDirectory;
                if (fbd.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                {
                    return new DirectoryInfo(fbd.SelectedPath);
                }
            }
            return null;
        }

#if NETCOREAPP3_1

        private async void Form1_Load(object sender, EventArgs e)
        {
            await Task.Delay(200);
            Compress();
        }

        private async void Compress()
        {
            DirectoryInfo imagesDir = GetDirectory();
            if (imagesDir == null)
            {
                Close();
                return;
            }

            DirectoryInfo outputDir = new DirectoryInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Optimized_Images"));
            outputDir.Create();

            var cts = new CancellationTokenSource();                    // Пользователь может остановить конвейер на любом этапе.

            var compressOpt = new ExecutionDataflowBlockOptions
            {
                EnsureOrdered = false,                                  // Сжимать файлы не в порядке поступления.              
                MaxDegreeOfParallelism = Environment.ProcessorCount,    // Максимально используем все ресурсы процессора.
                BoundedCapacity = Environment.ProcessorCount + 1,       // Держать на готове дополнительно один файл.
                CancellationToken = cts.Token,
            };

            var compressBlock = new TransformBlock<byte[], byte[]>(async rawJpeg =>   // Конвейер сжимающий файл в массив байт.
            {
                Debug.WriteLine("compressBlock");
                using (var inputStream = new MemoryStream(rawJpeg))
                using (var outputStream = new MemoryStream())
                {
                    await JpegOptim.Instance.CompressAsync(inputStream, outputStream, 75);
                    return outputStream.ToArray();
                }
            }, compressOpt);

            var displayResultBlock = new ActionBlock<byte[]>(imgBytes =>    // Конвейер отображает сжатое изображение на форме.
            {
                Debug.WriteLine("displayResultBlock");

                Image img;
                using (var mem = new MemoryStream(imgBytes))
                    img = Image.FromStream(mem);

                var picBox = new PictureBox();
                picBox.SizeMode = PictureBoxSizeMode.StretchImage;
                picBox.Size = new Size(100, 100);
                picBox.Image = img;
                flowLayoutPanel1.Controls.Add(picBox);

            }, new ExecutionDataflowBlockOptions { TaskScheduler = TaskScheduler.FromCurrentSynchronizationContext() });


            int fileIndexSeq = 0;
            var saveToDisk = new ActionBlock<byte[]>(async imgBytes => 
            {
                int uniqIndex = Interlocked.Increment(ref fileIndexSeq);
                Debug.WriteLine($"Сохраняем на диск №{uniqIndex}");
                await File.WriteAllBytesAsync(Path.Combine(outputDir.FullName, uniqIndex + ".jpg"), imgBytes);
            });

            var broadcastResult = new BroadcastBlock<byte[]>(clone => clone);

            // Соединить выход конвейера сжатия в UI конвеер и конвеер сохраняющий на диск.
            compressBlock.LinkTo(broadcastResult, new DataflowLinkOptions { PropagateCompletion = true });
            broadcastResult.LinkTo(displayResultBlock, new DataflowLinkOptions { PropagateCompletion = true });
            broadcastResult.LinkTo(saveToDisk, new DataflowLinkOptions { PropagateCompletion = true });

            try
            { 
                await Task.Run(async () =>
                {
                    foreach (FileInfo jpgPath in imagesDir.EnumerateFiles("*.jpg"))     // Все файлы в папке грузим в конвейер.
                    {
                        byte[] rawJpg = File.ReadAllBytes(jpgPath.FullName);
                        await compressBlock.SendAsync(rawJpg).ConfigureAwait(false);     // Передать файл в конвейер. Блокируется при достижении лимита.
                    }
                    compressBlock.Complete();
                });

                // Ждём когда будут отображены и сохранены все изображения.
                await Task.WhenAll(saveToDisk.Completion, displayResultBlock.Completion);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                cts?.Cancel();
            }
        }

        private async Task CompressImageAsync()
        {
            var cts = new CancellationTokenSource();
            try
            {
                var transformBlock = new TransformBlock<int, int>(quality =>
                {
                    Console.WriteLine($"Обработка №{quality}");
                    Thread.Sleep(quality * 100);
                    return quality;

                }, new ExecutionDataflowBlockOptions { EnsureOrdered = true, MaxDegreeOfParallelism = 6, CancellationToken = cts.Token });

                transformBlock.Post(85); // 2000 кБ
                transformBlock.Post(84); // 1900 кБ
                transformBlock.Post(83); // 1800 кБ
                transformBlock.Post(82); // 1700 кБ
                transformBlock.Post(81); // 1600 кБ
                transformBlock.Post(80); // 1500 кБ
                transformBlock.Post(79); // 1400 кБ
                transformBlock.Post(78); // 1200 кБ
                transformBlock.Post(77); // 1050 кБ
                transformBlock.Post(76); //  900 кБ *
                transformBlock.Post(75); //  600 кБ

                transformBlock.Complete();

                while (await transformBlock.OutputAvailableAsync())
                {
                    transformBlock.TryReceive(null, out int quality);
                    Console.WriteLine($"Завершен №{quality}");

                    if (quality == 76)
                    {
                        cts.Cancel();
                        cts = null;
                    }
                }
            }
            finally
            {
                cts?.Cancel();
            }
        }
#else
        private void Button_Compress_Click(object sender, EventArgs e)
        {

        }

        private async void Form1_Load(object sender, EventArgs e)
        {

        }
#endif
    }
}
