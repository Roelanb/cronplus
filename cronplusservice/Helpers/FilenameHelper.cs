using System;
using System.IO;
using System.Text;

namespace CronPlus.Helpers;

public static class FilenameHelper
{
    /// <summary>
    /// Translates a source filename to a destination filename using wildcard patterns.
    /// Supports '*' (any characters) and '?' (single character) wildcards in both name and extension parts.
    /// </summary>
    /// <param name="sourceFilePath">The full path of the source file</param>
    /// <param name="destinationPattern">The destination pattern with wildcards</param>
    /// <param name="destinationFolder">The destination folder path</param>
    /// <returns>The translated destination file path</returns>
    public static string TranslateFilename(string sourceFilePath, string destinationPattern, string destinationFolder)
    {
        if (string.IsNullOrEmpty(sourceFilePath))
            throw new ArgumentNullException(nameof(sourceFilePath));
        
        if (string.IsNullOrEmpty(destinationPattern))
            return Path.Combine(destinationFolder, Path.GetFileName(sourceFilePath));

        string sourceName = Path.GetFileNameWithoutExtension(sourceFilePath);
        string sourceExtension = Path.GetExtension(sourceFilePath);
        
        // If there's no extension separator in the pattern, treat it as just a name pattern
        if (!destinationPattern.Contains("."))
        {
            string translatedName = TranslatePattern(destinationPattern, sourceName);
            return Path.Combine(destinationFolder, translatedName + sourceExtension);
        }
        
        // Split the destination pattern into name and extension parts
        string[] parts = destinationPattern.Split('.');
        string namePattern = parts[0];
        string extensionPattern = parts.Length > 1 ? parts[1] : string.Empty;
        
        // Translate the name and extension using the patterns
        string newName = TranslatePattern(namePattern, sourceName);
        string newExtension = TranslatePattern(extensionPattern, sourceExtension.TrimStart('.'));
        
        // Combine the parts into the final path
        return Path.Combine(destinationFolder, newName + "." + newExtension);
    }
    
    private static string TranslatePattern(string pattern, string source)
    {
        if (string.IsNullOrEmpty(pattern))
            return source;
            
        if (!pattern.Contains("*") && !pattern.Contains("?"))
            return pattern;
            
        // If pattern is just "*", return the source unchanged
        if (pattern == "*")
            return source;
            
        // Process pattern character by character
        StringBuilder result = new StringBuilder();
        int sourceIndex = 0;
        bool starUsed = false;
        
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '*' && !starUsed)
            {
                // '*' means take the rest of the source, but only once
                if (sourceIndex < source.Length)
                {
                    result.Append(source.Substring(sourceIndex));
                    sourceIndex = source.Length;
                    starUsed = true;
                }
            }
            else if (c == '?')
            {
                // '?' means take one character from source at the corresponding position if available, otherwise use '_'
                if (i < source.Length && sourceIndex < source.Length)
                {
                    result.Append(source[i]);
                    sourceIndex = Math.Max(sourceIndex, i + 1);
                }
                else
                {
                    result.Append('_');
                }
            }
            else
            {
                result.Append(c);
                if (!starUsed && sourceIndex < source.Length && i < source.Length)
                {
                    sourceIndex = i + 1;
                }
            }
        }
        
        return result.ToString();
    }
}
