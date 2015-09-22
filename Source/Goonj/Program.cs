using System;

namespace Narkhedegs
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                ShowUsage();
            }
        }

        private static void ShowUsage()
        {
            Console.WriteLine("Goonj - echoes command-line arguments to stdout/stderr.");
            Console.WriteLine("");
            Console.WriteLine("Usage: goonj [options]");
            Console.WriteLine("");
            Console.WriteLine("Options:");
            Console.WriteLine("  --help             Displays how the tool is supposed to be used.");
        }
    }
}
