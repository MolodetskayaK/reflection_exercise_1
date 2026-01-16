using System;
using System.Diagnostics;
using System.Text;

public static class Program
{
    public static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        var obj = new F();

        int iterations = 100_000;
        int warmup = 5_000;

        IStringSerializer csv = new ReflectionCsvSerializer(';', '|');
        IStringSerializer json = new NewtonsoftJsonSerializer();

        string csvLine = csv.Serialize(obj);
        string jsonLine = json.Serialize(obj);

        Warmup(() => LoopSerialize(csv, obj, warmup));
        long csvSerMs = MeasureMs(() => LoopSerialize(csv, obj, iterations));

        Warmup(() => LoopDeserialize<F>(csv, csvLine, warmup));
        long csvDesMs = MeasureMs(() => LoopDeserialize<F>(csv, csvLine, iterations));

        Warmup(() => LoopSerialize(json, obj, warmup));
        long jsonSerMs = MeasureMs(() => LoopSerialize(json, obj, iterations));

        Warmup(() => LoopDeserialize<F>(json, jsonLine, warmup));
        long jsonDesMs = MeasureMs(() => LoopDeserialize<F>(json, jsonLine, iterations));

        long consolePrintMs = MeasureMs(() => PrintReport(
            iterations,
            csvLine, csvSerMs, csvDesMs,
            jsonLine, jsonSerMs, jsonDesMs
        ));

        Console.WriteLine();
        Console.WriteLine($"Время на вывод текста в консоль = {consolePrintMs} мс");
    }

    private static void PrintReport(
        int iterations,
        string csvLine, long csvSerMs, long csvDesMs,
        string jsonLine, long jsonSerMs, long jsonDesMs)
    {
        Console.WriteLine("Сериализуемый класс: class F");
        Console.WriteLine("{");
        Console.WriteLine("    int i1;");
        Console.WriteLine("    int i2;");
        Console.WriteLine("    int i3;");
        Console.WriteLine("    int i4;");
        Console.WriteLine("    int i5;");
        Console.WriteLine("    public int[] mas;");
        Console.WriteLine("    public F()");
        Console.WriteLine("    {");
        Console.WriteLine("        i1 = 1; i2 = 2; i3 = 3; i4 = 4; i5 = 5;");
        Console.WriteLine("        mas = new int[] { 1, 2 };");
        Console.WriteLine("    }");
        Console.WriteLine("    public F Get() => new F();");
        Console.WriteLine("}");
        Console.WriteLine();

        Console.WriteLine("код сериализации-десериализации:");
        Console.WriteLine(@"
var f = new F();

var csv = new ReflectionCsvSerializer(';', '|');
string s1 = csv.Serialize(f);
F f1 = csv.Deserialize<F>(s1);

var json = new NewtonsoftJsonSerializer();
string s2 = json.Serialize(f);
F f2 = json.Deserialize<F>(s2);
".Trim());
        Console.WriteLine();

        Console.WriteLine($"количество замеров: {iterations} итераций");
        Console.WriteLine();

        Console.WriteLine("мой рефлекшен (CSV):");
        Console.WriteLine($"Строка CSV = {csvLine}");
        Console.WriteLine($"Время на сериализацию = {csvSerMs} мс");
        Console.WriteLine($"Время на десериализацию = {csvDesMs} мс");
        Console.WriteLine();

        Console.WriteLine("стандартный механизм (NewtonsoftJson):");
        Console.WriteLine($"Строка JSON = {jsonLine}");
        Console.WriteLine($"Время на сериализацию = {jsonSerMs} мс");
        Console.WriteLine($"Время на десериализацию = {jsonDesMs} мс");
    }

    private static void Warmup(Action a)
    {
        a();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static long MeasureMs(Action a)
    {
        var sw = Stopwatch.StartNew();
        a();
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    private static void LoopSerialize(IStringSerializer s, object obj, int n)
    {
        int guard = 0;
        for (int i = 0; i < n; i++)
        {
            var str = s.Serialize(obj);
            if (str.Length > 0) guard ^= str[0];
        }
        if (guard == 123456789) Console.WriteLine("Impossible");
    }

    private static void LoopDeserialize<T>(IStringSerializer s, string data, int n) where T : new()
    {
        int guard = 0;
        for (int i = 0; i < n; i++)
        {
            var obj = s.Deserialize<T>(data);
            guard ^= obj.GetHashCode();
        }
        if (guard == 123456789) Console.WriteLine("Impossible");
    }
}
