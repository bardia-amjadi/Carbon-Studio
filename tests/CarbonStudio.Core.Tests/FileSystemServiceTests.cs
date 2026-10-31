using CarbonStudio.Core.FileSystem;

namespace CarbonStudio.Core.Tests;

public class FileSystemServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FileSystemService _fs = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void ListDirectory_PutsFoldersFirst_SortsCaseInsensitively_AndAppliesExclusions()
    {
        _dir.WriteFile("b.txt", "");
        _dir.WriteFile("A.txt", "");
        _dir.WriteFile("zeta/x.txt", "");
        _dir.WriteFile("node_modules/x.js", "");
        _dir.WriteFile("Alpha/x.txt", "");

        var names = _fs.ListDirectory(_dir.Path, ["NODE_MODULES"]).Select(e => e.Name).ToArray();

        Assert.Equal(["Alpha", "zeta", "A.txt", "b.txt"], names);
    }

    [Fact]
    public void CreateFile_And_CreateFolder_Work_AndRejectDuplicates()
    {
        var file = _fs.CreateFile(_dir.Path, _dir.Path, "a.txt");
        var folder = _fs.CreateFolder(_dir.Path, _dir.Path, "sub");

        Assert.True(File.Exists(file));
        Assert.True(Directory.Exists(folder));
        Assert.Throws<IOException>(() => _fs.CreateFile(_dir.Path, _dir.Path, "a.txt"));
        Assert.Throws<IOException>(() => _fs.CreateFolder(_dir.Path, _dir.Path, "sub"));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../escape.txt")]
    [InlineData("a/b.txt")]
    [InlineData("bad?.txt")]
    [InlineData("")]
    public void Create_RejectsUnsafeNames(string name)
    {
        Assert.Throws<ArgumentException>(() => _fs.CreateFile(_dir.Path, _dir.Path, name));
        Assert.Empty(Directory.GetFileSystemEntries(_dir.Path));
    }

    [Fact]
    public void Create_RejectsParentOutsideWorkspace()
    {
        var root = _dir.Combine("ws");
        Directory.CreateDirectory(root);
        Assert.Throws<ArgumentException>(() => _fs.CreateFile(root, _dir.Path, "x.txt"));
    }

    [Fact]
    public void Rename_MovesFileAndFolder()
    {
        var file = _dir.WriteFile("old.txt", "data");
        var renamed = _fs.Rename(_dir.Path, file, "new.txt");
        Assert.Equal("new.txt", Path.GetFileName(renamed));
        Assert.False(File.Exists(file));
        Assert.Equal("data", File.ReadAllText(renamed));

        var folder = _dir.Combine("dir1");
        Directory.CreateDirectory(folder);
        Assert.True(Directory.Exists(_fs.Rename(_dir.Path, folder, "dir2")));
    }

    [Fact]
    public void Rename_RejectsExistingTarget_RootAndOutsidePaths()
    {
        var a = _dir.WriteFile("a.txt", "");
        _dir.WriteFile("b.txt", "");

        Assert.Throws<IOException>(() => _fs.Rename(_dir.Path, a, "b.txt"));
        Assert.Throws<ArgumentException>(() => _fs.Rename(_dir.Path, _dir.Path, "x"));

        var outside = Path.Combine(Path.GetTempPath(), "carbon-outside.txt");
        Assert.Throws<ArgumentException>(() => _fs.Rename(_dir.Path, outside, "x.txt"));
    }

    [Fact]
    public void Rename_ToSameName_IsNoOp()
    {
        var a = _dir.WriteFile("a.txt", "keep");
        Assert.Equal(a, _fs.Rename(_dir.Path, a, "a.txt"));
        Assert.Equal("keep", File.ReadAllText(a));
    }

    [Fact]
    public void Delete_RemovesFilesAndFolders_ButNeverTheRoot()
    {
        var file = _dir.WriteFile("f.txt", "");
        _dir.WriteFile("d/inner/x.txt", "");

        _fs.Delete(_dir.Path, file, sendToRecycleBin: false);
        _fs.Delete(_dir.Path, _dir.Combine("d"), sendToRecycleBin: false);

        Assert.Empty(Directory.GetFileSystemEntries(_dir.Path));
        Assert.Throws<ArgumentException>(() => _fs.Delete(_dir.Path, _dir.Path, sendToRecycleBin: false));
        Assert.True(Directory.Exists(_dir.Path));
    }

    [Fact]
    public void Delete_Missing_ThrowsFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() => _fs.Delete(_dir.Path, _dir.Combine("ghost.txt"), sendToRecycleBin: false));
    }
}
