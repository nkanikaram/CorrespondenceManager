using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace CustomerCorrespondence.Domain.Entities
{
    /// <summary>
    /// Shared helpers for sanitizing and validating ProcessCorrespondenceRequest payloads.
    /// </summary>
    internal static class ProcessRequestHelper
    {
        // Matches characters that can be weaponised for injection attacks: < > { } [ ] ; | \ ^ ~ `
        internal static readonly Regex ForbiddenCharsRegex =
            new Regex(@"[<>{}\[\];|\\^~`]", RegexOptions.Compiled);

        /// <summary>
        /// Throws a <see cref="ValidationException"/> if <paramref name="value"/> contains forbidden characters.
        /// </summary>
        internal static void CheckField(string? value, string fieldName)
        {
            if (value != null && ForbiddenCharsRegex.IsMatch(value))
                throw new ValidationException(
                    $"{fieldName} contains invalid special characters. " +
                    @"Forbidden characters: < > { } [ ] ; | \ ^ ~ `");
        }
    }

    //internal static class ProcessRequestHelper
    //{
    //    // Matches characters that can be weaponised for injection attacks: < > { } [ ] ; | \ ^ ~ `
    //    internal static readonly Regex ForbiddenCharsRegex =
    //        new Regex(@"[<>{}\[\];|\\^~`]", RegexOptions.Compiled);

    //    // Same as above but allows backslash (for file path fields)
    //    internal static readonly Regex ForbiddenCharsRegexAllowBackslash =
    //        new Regex(@"[<>{}\[\];|^~`]", RegexOptions.Compiled);

    //    /// <summary>
    //    /// Fields that are allowed to contain backslashes (file paths).
    //    /// </summary>
    //    private static readonly HashSet<string> PathFields = new(StringComparer.OrdinalIgnoreCase)
    //{
    //    "AdditionalData[PrintLocalPath]",
    //    "AdditionalData[PrintLocalFileName]"
    //};

    //    /// <summary>
    //    /// Throws a <see cref="ValidationException"/> if <paramref name="value"/> contains forbidden characters.
    //    /// </summary>
    //    internal static void CheckField(string? value, string fieldName)
    //    {
    //        if (value == null) return;

    //        var regex = PathFields.Contains(fieldName)
    //            ? ForbiddenCharsRegexAllowBackslash
    //            : ForbiddenCharsRegex;

    //        if (regex.IsMatch(value))
    //            throw new ValidationException(
    //                $"{fieldName} contains invalid special characters. " +
    //                @"Forbidden characters: < > { } [ ] ; | \ ^ ~ `");
    //    }
    //}
}