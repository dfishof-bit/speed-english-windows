using System.IO;
using System.Reflection;
using System.Text;

namespace SpeedEnglish;

public sealed record WordPair(string English, string Hebrew);

public static class WordRepository
{
    public static List<WordPair> LoadEmbeddedWords()
    {
        var enLines = ReadEmbeddedLines("enheb.txt");
        var heLines = ReadEmbeddedLines("heb.txt");
        var count = Math.Min(enLines.Count, heLines.Count);
        var list = new List<WordPair>(count);

        for (int i = 0; i < count; i++)
        {
            var en = enLines[i].Trim();
            var he = heLines[i].Trim();
            if (en.Length > 0 && he.Length > 0)
            {
                list.Add(new WordPair(en, he));
            }
        }
        return list;
    }

    private static List<string> ReadEmbeddedLines(string suffix)
    {
        var asm = Assembly.GetExecutingAssembly();
        var resName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException("Embedded dictionary not found: " + suffix);

        using var stream = asm.GetManifestResourceStream(resName)!;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lines = new List<string>(50016);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (lines.Count == 0 && line.StartsWith('\uFEFF'))
            {
                line = line[1..];
            }
            lines.Add(line);
        }
        return lines;
    }

    public static string? ExtractCelebrationAudioToTemp()
    {
        try
        {
            var outPath = Path.Combine(Path.GetTempPath(), "SpeedEnglish_ca1.mp3");
            if (File.Exists(outPath) && new FileInfo(outPath).Length > 1000)
            {
                return outPath;
            }
            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("ca1.mp3", StringComparison.OrdinalIgnoreCase));
            if (resName is null) return null;

            using var stream = asm.GetManifestResourceStream(resName)!;
            using var fs = File.Create(outPath);
            stream.CopyTo(fs);
            return outPath;
        }
        catch
        {
            return null;
        }
    }
}
