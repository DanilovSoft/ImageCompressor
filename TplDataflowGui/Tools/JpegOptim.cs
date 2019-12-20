using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DanilovSoft
{
    public sealed class JpegOptim
    {
        public static readonly JpegOptim Instance = new JpegOptim();
        private static readonly FileInfo _jpegoptim;

        static JpegOptim()
        {
            var filePath = new FileInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "jpegoptim.exe"));
            if (!filePath.Exists)
                throw new FileNotFoundException("Не найден файл jpegoptim.exe", filePath.FullName);

            _jpegoptim = filePath;
        }

        public JpegOptim()
        {

        }

        private static string Arguments(int maximumQuality) => $"-m{maximumQuality} --strip-all --all-progressive --stdin --stdout";
        private static string ArgumentsLossless() => $"--strip-all --all-progressive --stdin --stdout";

        private async Task StdInOutCopyAsync(Process proc, Stream inputStream, Stream outputStream)
        {
            // С параметром --stdout любые сообщения будут выводиться в StandardError.
            Task<string> errorTask = Task.Run(() => proc.StandardError.ReadToEndAsync());

            // Поток копирует stdout в output Stream
            Task stdoutTask = Task.Run(() => proc.StandardOutput.BaseStream.CopyToAsync(outputStream));

            try
            {
                // Копируем input Stream в stdin.
                await inputStream.CopyToAsync(proc.StandardInput.BaseStream).ConfigureAwait(false);

                // Нужно закрыть stdin что-бы процесс понял что достигнут EOF.
                proc.StandardInput.Close();
            }
            catch (IOException)
            {
                // Может происходить исключение закрытия стрима.
                // Происходит потому что stdout отдал все данные и процес завершился, несмотря на то
                // что мы записали не все данные в stdin. Процесс понимает когда полезных данных больше нет.
                // Удостовериться что всё в порядке следует по коду возврата процесса.
            }
            catch (Exception)
            // Не смогли записать в stdin.
            {
                try
                {
                    // Нужно подождать завершение работы с outputStream.
                    await stdoutTask.ConfigureAwait(false);
                }
                catch { }

                throw;
            }
            await FinishProcessAsync(proc, errorTask, stdoutTask).ConfigureAwait(false);
        }

        /// <summary>
        /// Сжимает с потерями, затем выполняет lossless сжатие для пущего уменьшения размера.
        /// </summary>
        /// <exception cref="PrematureEndOfFileException"/>
        public async Task CompressAsync(Stream inputStream, Stream outputStream, int maximumQuality)
        {
            // PS. Размер stdin или stdout имеет константный буфер 4096 байт.
            string args = Arguments(maximumQuality);
            using (Process proc = StartProcess(args))
            {
                await StdInOutCopyAsync(proc, inputStream, outputStream).ConfigureAwait(false);
            }
        }

        private Process StartProcess(string arguments)
        {
            var proc = new Process();
            proc.StartInfo.FileName = _jpegoptim.FullName;
            proc.StartInfo.Arguments = arguments;
            proc.StartInfo.UseShellExecute = false; //required to redirect standart input/output
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.RedirectStandardInput = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError = true;
            proc.Start();
            proc.PriorityClass = ProcessPriorityClass.BelowNormal;
            return proc;
        }

        public void OptimizeLossless(Stream inputStream, Stream outputStream)
        {
            string args = ArgumentsLossless();
            using (Process proc = StartProcess(args))
            {
                // С параметром --stdout любые сообщения будут выводиться в StandardError.
                Task<string> errorTask = Task.Factory.StartNew(() => proc.StandardError.ReadToEndAsync(), TaskCreationOptions.LongRunning).Unwrap();

                // Поток копирует stdout в output Stream
                Task stdoutTask = Task.Factory.StartNew(() => proc.StandardOutput.BaseStream.CopyToAsync(outputStream), TaskCreationOptions.LongRunning).Unwrap();

                try
                {
                    Copy(inputStream, proc.StandardInput.BaseStream);

                    // Копируем input Stream в stdin.
                    //inputStream.CopyTo(proc.StandardInput.BaseStream);

                    // Нужно закрыть stdin что-бы процесс понял что достигнут EOF.
                    try
                    {
                        proc.StandardInput.Close();
                    }
                    catch (Exception)
                    {

                    }

                }
                catch (Exception ex)
                // Не смогли записать в stdin.
                {
                    // Нужно подождать завершение работы с outputStream.
                    stdoutTask.GetAwaiter().GetResult();

                    throw;
                }
                FinishProcessAsync(proc, errorTask, stdoutTask);
            }
        }

        private static void Copy(Stream a, Stream b)
        {
            while (true)
            {
                var buf = new byte[8096];
                int n = a.Read(buf, 0, buf.Length);
                if (n == 0)
                    return;

                b.Write(buf, 0, n);
            }
        }

        private static async Task FinishProcessAsync(Process proc, Task<string> errorTask, Task stdoutTask)
        {
            // Дождаться stdout.
            await Task.WhenAll(stdoutTask, errorTask).ConfigureAwait(false);

            proc.WaitForExit();
            string errorLine = errorTask.Result;

            if (proc.ExitCode != 0)
            {
                errorLine = errorLine?.Trim();
                if (errorLine != "")
                    throw new InvalidOperationException(errorLine);

                throw new InvalidOperationException("ExitCode: " + proc.ExitCode);
            }
            else
            {
                //Debug.WriteLine(errorLine);
            }
        }

        private async Task<string> ReadErrorAsync(StreamReader reader)
        {
            string s = await reader.ReadLineAsync().ConfigureAwait(false);
            return s;
        }
    }
}