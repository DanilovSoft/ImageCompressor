#if NETCOREAPP3_1
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DanilovSoft
{
    public class MozJpeg
    {
        private const string LosslessExePath = "jpegtran-static.exe";
        private const string LossyExePath = "cjpeg-static.exe";
        public static readonly MozJpeg Instance = new MozJpeg();
        private static readonly FileInfo _losslessExe;
        private static readonly FileInfo _lossyExe;

        static MozJpeg()
        {
            var filePath = new FileInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", LosslessExePath));
            if (!filePath.Exists)
                throw new FileNotFoundException($"Не найден файл {LosslessExePath}", filePath.FullName);

            _losslessExe = filePath;

            var filePath2 = new FileInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", LossyExePath));
            if (!filePath2.Exists)
                throw new FileNotFoundException($"Не найден файл {LossyExePath}", filePath2.FullName);

            _lossyExe = filePath2;
        }

        private MozJpeg() { }

        private Process StartProcess(string fileName, string arguments)
        {
            var proc = new Process();
            proc.StartInfo.FileName = fileName;
            proc.StartInfo.Arguments = arguments;
            proc.StartInfo.UseShellExecute = false; //required to redirect standart input/output
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.RedirectStandardInput = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError = true;
            proc.Start();
            proc.PriorityClass = ProcessPriorityClass.AboveNormal;
            return proc;
        }

        private Process StartProcessLossy(string arguments)
        {
            var proc = new Process();
            proc.StartInfo.FileName = _lossyExe.FullName;
            proc.StartInfo.Arguments = arguments;
            proc.StartInfo.UseShellExecute = false; //required to redirect standart input/output
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.RedirectStandardInput = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError = true;
            proc.Start();
            proc.PriorityClass = ProcessPriorityClass.AboveNormal;
            return proc;
        }

        private static string ArgumentsLossless() => $"-copy none -optimize -progressive";
        private static string Arguments(int maximumQuality) => $"-quality {maximumQuality}";

        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="PrematureEndOfFileException"/>
        public Task CompressLosslessAsync(Stream inputStream, Stream outputStream, CancellationToken cancellationToken)
        {
            return InnerCompressLosslessAsync(inputStream, outputStream, cancellationToken);
        }

        private async Task InnerCompressLosslessAsync(Stream inputStream, Stream outputStream, CancellationToken cancellationToken)
        {
            string args = ArgumentsLossless();
            using (Process proc = StartProcess(_losslessExe.FullName, args))
            {
                await StdInOutCopyAsync(proc, inputStream, outputStream, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task StdInOutCopyAsync(Process proc, Stream inputStream, Stream outputStream, CancellationToken cancellationToken)
        {
            // С параметром --stdout любые сообщения будут выводиться в StandardError.
            Task<string> errorTask = Task.Run(() => proc.StandardError.ReadToEndAsync(), cancellationToken);

            // Поток копирует stdout в output Stream
            Task stdoutTask = Task.Run(() => proc.StandardOutput.BaseStream.CopyToAsync(outputStream, cancellationToken), cancellationToken);

            try
            {
                // Копируем input Stream в stdin.
                await inputStream.CopyToAsync(proc.StandardInput.BaseStream, cancellationToken).ConfigureAwait(false);

                // Нужно закрыть stdin что-бы процесс понял что достигнут EOF.
                proc.StandardInput.Close();
            }
            catch (IOException)
            {
                // Может происходить исключение закрытия стрима.
                // Происходит потому что stdout отдал все данные и процес завершился, несмотря на то
                // что мы записали не все данные в stdin. Вероятно процесс понимает что полезных данных там не будет.
                // Удостоверится что всё в порядке следует по коду возврата процесса.
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

        public async Task<byte[]> CompressAsync(byte[] input, int maximumQuality, CancellationToken cancellationToken = default)
        {
            using (var inputStream = new MemoryStream(input))
            using (var outputStream = new MemoryStream())
            {
                await CompressAsync(inputStream, outputStream, maximumQuality, cancellationToken).ConfigureAwait(false);
                return outputStream.ToArray();
            }
        }

        /// <summary>
        /// Сжимает с потерями, затем выполняет lossless сжатие для пущего уменьшения размера.
        /// </summary>
        /// <exception cref="PrematureEndOfFileException"/>
        public async Task CompressAsync(Stream inputStream, Stream outputStream, int maximumQuality, CancellationToken cancellationToken)
        {
            // PS. Размер stdin или stdout имеет константный буфер 4096 байт.

            //using (var mem = new MemoryStream((int)inputStream.Length))
            {
                string args = Arguments(maximumQuality);
                using (Process proc = StartProcessLossy(args))
                {
                    await StdInOutCopyAsync(proc, inputStream, outputStream, cancellationToken).ConfigureAwait(false);
                }

                //mem.Position = 0;

                // tip: run lossless compression after lossy for best results.
                //await CompressLosslessAsync(mem, outputStream).ConfigureAwait(false);
            }
        }

        private static async Task FinishProcessAsync(Process proc, Task<string> errorTask, Task stdoutTask)
        {
            // Дождаться stdout.
            await stdoutTask.ConfigureAwait(false);

            proc.WaitForExit();
            string errorLine = (await errorTask.ConfigureAwait(false))?.Trim();

            if (proc.ExitCode == 0)
            // Процесс успешно завершен.
            {
                //Debug.WriteLine(errorLine);
            }
            else
            {
                if (errorLine != "")
                    throw new InvalidOperationException(errorLine);

                throw new InvalidOperationException("ExitCode: " + proc.ExitCode);
            }
        }
    }
}

#endif