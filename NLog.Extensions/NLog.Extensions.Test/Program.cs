using System;
using System.Threading.Tasks;

namespace NLog.Extensions.Test
{
    class Program
    {
        static readonly Logger logger = LogManager.GetCurrentClassLogger();
        static void Main(string[] args)
        {
            try
            {
                Task.Run(() =>
                {
                    string[] a = null;
                    Console.WriteLine(string.Join(",", a));
                }).Wait();
            }
            catch (Exception ex)
            {
                logger.Debug("Test", ex);
            }
            Console.ReadLine();
        }
    }
}
