using System;
using CronPlus.Helpers;
using Xunit;

namespace CronPlus.Tests;

public class FilenameHelperTests
{
    [Fact]
    public void TranslateFilename_NoWildcards_ReturnsPatternWithFolder()
    {
        // Arrange
        string sourcePath = "/path/to/source.txt";
        string destPattern = "dest.txt";
        string destFolder = "/path/to/destination";

        // Act
        string result = FilenameHelper.TranslateFilename(sourcePath, destPattern, destFolder);

        // Assert
        Xunit.Assert.Equal("/path/to/destination/dest.txt", result.Replace("\\", "/"));
    }

    [Xunit.Fact]
    public void TranslateFilename_StarWildcardInName_UsesSourceName()
    {
        // Arrange
        string sourcePath = "/path/to/source.txt";
        string destPattern = "*.txt";
        string destFolder = "/path/to/destination";

        // Act
        string result = FilenameHelper.TranslateFilename(sourcePath, destPattern, destFolder);

        // Assert
        Xunit.Assert.Equal("/path/to/destination/source.txt", result.Replace("\\", "/"));
    }

    [Xunit.Fact]
    public void TranslateFilename_StarWildcardInExtension_UsesSourceExtension()
    {
        // Arrange
        string sourcePath = "/path/to/source.txt";
        string destPattern = "dest.*";
        string destFolder = "/path/to/destination";

        // Act
        string result = FilenameHelper.TranslateFilename(sourcePath, destPattern, destFolder);

        // Assert
        Xunit.Assert.Equal("/path/to/destination/dest.txt", result.Replace("\\", "/"));
    }

    [Xunit.Fact]
    public void TranslateFilename_QuestionMarkWildcard_ReplacesWithSourceChars()
    {
        // Arrange
        string sourcePath = "/path/to/source.txt";
        string destPattern = "d??t.txt";
        string destFolder = "/path/to/destination";

        // Act
        string result = FilenameHelper.TranslateFilename(sourcePath, destPattern, destFolder);

        // Assert
        Xunit.Assert.Equal("/path/to/destination/dout.txt", result.Replace("\\", "/"));
    }

    [Xunit.Fact]
    public void TranslateFilename_EmptyPattern_ReturnsSourceNameWithFolder()
    {
        // Arrange
        string sourcePath = "/path/to/source.txt";
        string destPattern = string.Empty;
        string destFolder = "/path/to/destination";

        // Act
        string result = FilenameHelper.TranslateFilename(sourcePath, destPattern, destFolder);

        // Assert
        Xunit.Assert.Equal("/path/to/destination/source.txt", result.Replace("\\", "/"));
    }

    [Xunit.Fact]
    public void TranslateFilename_NoExtensionInPattern_UsesSourceExtension()
    {
        // Arrange
        string sourcePath = "/path/to/source.txt";
        string destPattern = "dest";
        string destFolder = "/path/to/destination";

        // Act
        string result = FilenameHelper.TranslateFilename(sourcePath, destPattern, destFolder);

        // Assert
        Xunit.Assert.Equal("/path/to/destination/dest.txt", result.Replace("\\", "/"));
    }

    [Xunit.Fact]
    public void TranslateFilename_ExcessQuestionMarks_ReplacesWithUnderscore()
    {
        // Arrange
        string sourcePath = "/path/to/source.txt";
        string destPattern = "d?????t.txt";
        string destFolder = "/path/to/destination";

        // Act
        string result = FilenameHelper.TranslateFilename(sourcePath, destPattern, destFolder);

        // Assert
        Xunit.Assert.Equal("/path/to/destination/dourcet.txt", result.Replace("\\", "/"));
    }
}
