using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Context.Tests;

// Job 284: the committed icon files, their place in the build output, the window icon lookup and its use in Program.cs.
public sealed class AppIconTests
{
    private const string AssetsFolder = "src/Context.Desktop/Assets";

    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    // AC-1: a valid PNG, square and at least 256x256.
    [Fact]
    public void Png_IsASquarePngOfAtLeast256Pixels()
    {
        byte[] png = ReadAsset("Context.png");

        (int width, int height) = PngSize(png);
        Assert.Equal(width, height);
        Assert.True(width >= 256, "Context.png is " + width + " px wide");
    }

    // AC-2: an ICO header, the sizes 16, 32, 48 and 256 (stored as 0), every entry inside the file.
    [Fact]
    public void Ico_HoldsTheWindowsSizes_AndEveryEntryLiesInsideTheFile()
    {
        byte[] ico = ReadAsset("Context.ico");

        Assert.True(ico.Length >= 6, "Context.ico is too short for a header");
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(0)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        Assert.True(count > 0, "Context.ico has no images");
        Assert.True(6 + (16 * count) <= ico.Length, "the directory runs past the end of Context.ico");

        HashSet<int> sizes = [];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = ico.AsSpan(6 + (16 * i), 16);
            int width = entry[0] == 0 ? 256 : entry[0];
            int height = entry[1] == 0 ? 256 : entry[1];
            Assert.Equal(width, height);
            long length = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            long offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            Assert.True(length > 0, "entry " + i + " is empty");
            Assert.True(offset >= 6 + (16 * count) && offset + length <= ico.Length, "entry " + i + " lies outside the file");
            sizes.Add(width);
        }

        Assert.Superset(new HashSet<int> { 16, 32, 48, 256 }, sizes);
    }

    // AC-3: the icns header length equals the file size, the chunks walk exactly to the end, a 1024 or 512 px chunk
    // and a 32 px chunk.
    [Fact]
    public void Icns_ChunksWalkToTheEnd_AndHoldTheLargeAndSmallSizes()
    {
        byte[] icns = ReadAsset("Context.icns");

        Assert.True(icns.Length >= 8, "Context.icns is too short for a header");
        Assert.Equal("icns", Encoding.ASCII.GetString(icns, 0, 4));
        Assert.Equal((uint)icns.Length, BinaryPrimitives.ReadUInt32BigEndian(icns.AsSpan(4)));

        List<string> types = [];
        int at = 8;
        while (at < icns.Length)
        {
            Assert.True(at + 8 <= icns.Length, "a chunk header runs past the end at " + at);
            string type = Encoding.ASCII.GetString(icns, at, 4);
            uint length = BinaryPrimitives.ReadUInt32BigEndian(icns.AsSpan(at + 4));
            Assert.True(length >= 8 && at + length <= icns.Length, "chunk " + type + " runs past the end");
            types.Add(type);
            at += (int)length;
        }

        Assert.Equal(icns.Length, at);
        Assert.True(types.Contains("ic10") || types.Contains("ic09"), "no 1024 or 512 px chunk: " + string.Join(",", types));
        Assert.True(
            types.Contains("ic05") || types.Contains("ic11") || types.Contains("il32"),
            "no 32 px chunk: " + string.Join(",", types));
    }

    // AC-4: the source artwork is XML with an <svg> root.
    [Fact]
    public void Svg_IsXmlWithAnSvgRoot()
    {
        string path = Path.Combine(RepoFiles.Root(), AssetsFolder, "context-icon.svg");
        XmlReaderSettings settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using XmlReader reader = XmlReader.Create(path, settings);
        XElement root = XDocument.Load(reader).Root!;

        Assert.Equal("svg", root.Name.LocalName);
    }

    // AC-5: the csproj names the exe icon, and the build output holds the three icon files but not the SVG.
    [Fact]
    public void Build_NamesTheExeIcon_AndCopiesTheIconFilesButNotTheSvg()
    {
        string root = RepoFiles.Root();
        string project = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "Context.Desktop.csproj"));
        Assert.Contains("<ApplicationIcon>Assets\\Context.ico</ApplicationIcon>", project, StringComparison.Ordinal);

        // This test project's output is tests/Context.Desktop.Tests/bin/<config>/net10.0/; the app's is built in the
        // same configuration.
        DirectoryInfo testOutput = new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        string configuration = testOutput.Parent!.Name;
        string assets = Path.Combine(root, RepoFiles.DesktopFolder, "bin", configuration, testOutput.Name, "Assets");

        Assert.True(Directory.Exists(assets), "no Assets folder in " + assets);
        List<string> files = [.. Directory.GetFiles(assets).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];
        Assert.Equal(["Context.icns", "Context.ico", "Context.png"], files);
        foreach (string file in files)
        {
            Assert.Equal(ReadAsset(file), File.ReadAllBytes(Path.Combine(assets, file)));
        }
    }

    // AC-6: the .ico on Windows, the .png elsewhere, null when that file is missing.
    [Theory]
    [InlineData(true, "Context.ico", "Context.png")]
    [InlineData(false, "Context.png", "Context.ico")]
    public void File_IsThePlatformsIcon_OrNullWhenItIsMissing(bool windows, string own, string other)
    {
        string folder = Path.Combine(Path.GetTempPath(), "context-icon-" + Guid.NewGuid().ToString("N"));
        try
        {
            string assets = Path.Combine(folder, "Assets");
            Directory.CreateDirectory(assets);
            File.WriteAllBytes(Path.Combine(assets, other), [1]);
            Assert.Null(AppIcon.File(folder, windows));

            File.WriteAllBytes(Path.Combine(assets, own), [1]);
            Assert.Equal(Path.Combine(folder, "Assets", own), AppIcon.File(folder, windows));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // AC-6: no Assets folder at all is no icon.
    [Fact]
    public void File_IsNullWithoutAnAssetsFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "context-icon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Assert.Null(AppIcon.File(folder, windows: true));
            Assert.Null(AppIcon.File(folder, windows: false));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // AC-7: Program.cs sets the icon only from AppIcon.File(AppContext.BaseDirectory, OperatingSystem.IsWindows()),
    // after a null check and inside a try whose catch keeps start-up going.
    [Fact]
    public void Program_SetsTheWindowIconOnlyThroughAppIcon_GuardedAndCaught()
    {
        string program = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "Program.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Single(Regex.Matches(program, @"\bSetIcon\w*\s*\("));
        Assert.DoesNotContain("SetIconFile(\"", program, StringComparison.Ordinal);
        Assert.Matches(
            @"string\? (\w+) = AppIcon\.File\(AppContext\.BaseDirectory, OperatingSystem\.IsWindows\(\)\);\s*"
                + @"if \(\1 is not null\)\s*\{\s*try\s*\{\s*app\.MainWindow\.SetIconFile\(\1\);\s*\}\s*"
                + @"catch \(Exception\)\s*\{[^{}]*\}\s*\}",
            program);
    }

    private static byte[] ReadAsset(string name) => File.ReadAllBytes(Path.Combine(RepoFiles.Root(), AssetsFolder, name));

    private static (int Width, int Height) PngSize(byte[] png)
    {
        Assert.True(png.Length >= 33, "too short for a PNG header");
        Assert.Equal(PngSignature, png[..8]);
        Assert.Equal(13u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(8)));
        Assert.Equal("IHDR", Encoding.ASCII.GetString(png, 12, 4));
        return ((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16)), (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20)));
    }
}
