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
using System.Reflection;
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

        private void Form1_Load_1(object sender, EventArgs e)
        {
            
        }

#if NETCOREAPP3_1

        private async Task CompressAsync(int? threadsLimit)
        {
            DirectoryInfo imagesDir = GetDirectory();
            if (imagesDir == null)
            {
                Close();
                return;
            }

            var outputDir = new DirectoryInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Optimized_Images"));
            outputDir.Create();

            var cts = new CancellationTokenSource();                    // Пользователь может остановить конвейер на любом этапе.

            var compressionOpt = new ExecutionDataflowBlockOptions
            {
                EnsureOrdered = false,                              // Возвращать файлы не в порядке поступления.              
                MaxDegreeOfParallelism = threadsLimit ?? Environment.ProcessorCount,
                BoundedCapacity = Environment.ProcessorCount + 1,   // Держать на готове дополнительно один файл.
                CancellationToken = cts.Token,
            };

            var compressionBlock = new TransformBlock<byte[], byte[]>(async rawJpeg =>   // Конвейер сжимающий файл в массив байт.
            {
                Debug.WriteLine("compressBlock");
                using (var inputStream = new MemoryStream(rawJpeg))
                using (var outputStream = new MemoryStream())
                {
                    await JpegOptim.Instance.CompressAsync(inputStream, outputStream, maximumQuality: 75);
                    return outputStream.ToArray();
                }
            }, compressionOpt);

            var displayImageBlock = new ActionBlock<Image>(img =>    // Конвейер отображает сжатое изображение на форме.
            {
                Debug.WriteLine("displayResultBlock");
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
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = Environment.ProcessorCount });

            var broadcastResult = new BroadcastBlock<byte[]>(clone => clone);

            var prepareUIImage = new TransformBlock<byte[], Image>(imgBytes => 
            {
                using (var mem = new MemoryStream(imgBytes))
                    return Image.FromStream(mem);
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = Environment.ProcessorCount });

            var linkToOpt = new DataflowLinkOptions { PropagateCompletion = true };
            
            compressionBlock.LinkTo(broadcastResult, linkToOpt);    // Сжатое изображение направить в Broadcast.
            broadcastResult.LinkTo(saveToDisk, linkToOpt);          // Копию сжатого изображения сохранить на диск.
            broadcastResult.LinkTo(prepareUIImage, linkToOpt);      // Копию сжатого изображения преобразовать в объект Image.
            prepareUIImage.LinkTo(displayImageBlock, linkToOpt);    // Объект Image отобразить в UI потоке.

            try
            { 
                // Уйти из UI потока.
                await Task.Run(async () =>
                {
                    foreach (FileInfo jpgPath in imagesDir.EnumerateFiles("*.jpg"))         // Все файлы в папке грузим в конвейер.
                    {
                        byte[] rawJpg = await File.ReadAllBytesAsync(jpgPath.FullName).ConfigureAwait(false);
                        await compressionBlock.SendAsync(rawJpg).ConfigureAwait(false);     // Передать файл в конвейер. Блокируется при достижении лимита.
                    }
                    compressionBlock.Complete();

                    // Ждём когда будут отображены и сохранены все изображения.
                    await Task.WhenAll(saveToDisk.Completion, displayImageBlock.Completion);
                });
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

        private async void Button_Compress_Click(object sender, EventArgs e)
        {
            int? threadsLimit = checkBox1.Checked ? (int?)null : 1;

            button1.Enabled = false;
            checkBox1.Enabled = false;
            await CompressAsync(threadsLimit);
            button1.Enabled = true;
            checkBox1.Enabled = true;
        }

        private async Task CompressBySizeAsync()
        {
            var cts = new CancellationTokenSource();
            try
            {
                var compressionBlock = new TransformBlock<int, int>(quality =>
                {
                    Console.WriteLine($"Обработка №{quality}");
                    Thread.Sleep(quality * 100);
                    return quality;

                }, new ExecutionDataflowBlockOptions { EnsureOrdered = true, MaxDegreeOfParallelism = 6, CancellationToken = cts.Token });

                compressionBlock.Post(85); // 2000 кБ
                compressionBlock.Post(84); // 1900 кБ
                compressionBlock.Post(83); // 1800 кБ
                compressionBlock.Post(82); // 1700 кБ
                compressionBlock.Post(81); // 1600 кБ
                compressionBlock.Post(80); // 1500 кБ
                compressionBlock.Post(79); // 1400 кБ
                compressionBlock.Post(78); // 1200 кБ
                compressionBlock.Post(77); // 1050 кБ
                compressionBlock.Post(76); //  900 кБ *
                compressionBlock.Post(75); //  600 кБ

                compressionBlock.Complete();

                while (await compressionBlock.OutputAvailableAsync())
                {
                    compressionBlock.TryReceive(null, out int quality);
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

        private void button1_Click(object sender, EventArgs e)
        {

        }
#endif
    }
}
