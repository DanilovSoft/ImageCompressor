using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace ConsoleApp
{
    class Program
    {
        static Task Main(string[] args)
        {
            return CompressBySizeAsync();
        }

        private static async Task CompressBySizeAsync()
        {
            var cts = new CancellationTokenSource();
            try
            {
                var compressionBlock = new TransformBlock<int, int>(quality =>
                {
                    Console.WriteLine($"Обработка №{quality}");
                    Thread.Sleep(quality * 100);
                    return quality;

                }, 
                new ExecutionDataflowBlockOptions 
                { 
                    EnsureOrdered = true, 
                    MaxDegreeOfParallelism = Environment.ProcessorCount, 
                    CancellationToken = cts.Token 
                });

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
                        break;
                    }
                }
            }
            finally
            {
                cts?.Cancel();
            }
            Thread.Sleep(-1);
        }
    }
}
