using System.Text;

namespace HR.Modules.Platform.Services.SemanticCatalog;

/// <summary>Pure Arabic text normalization for search matching: unify alef/taa-marbuta/alef-maqsura,
/// strip tashkeel (diacritics) + tatweel, and lowercase Latin. Not for display.</summary>
public static class ArabicText
{
    public static string Normalize(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            switch (ch)
            {
                case 'أ': case 'إ': case 'آ': case 'ٱ': sb.Append('ا'); break;
                case 'ة': sb.Append('ه'); break;
                case 'ى': sb.Append('ي'); break;
                case 'ـ': break;                          // tatweel
                case >= 'ً' and <= 'ْ': break;  // tashkeel (fathatan..sukun)
                case 'ٰ': break;                     // superscript alef
                default: sb.Append(char.ToLowerInvariant(ch)); break;
            }
        }
        return sb.ToString();
    }
}
