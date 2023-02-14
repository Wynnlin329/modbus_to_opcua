namespace modbus_to_opcua
{
    internal class Program
    {
        static void Main(string[] args)
        {
            //Console.WriteLine("***Modbus Gateway***");
            //Console.WriteLine("按下enter鍵開始轉換");
            //Console.ReadLine();
            new main().ProgramStart();
            while (true) { Thread.Sleep(100); };
        }
    }
}