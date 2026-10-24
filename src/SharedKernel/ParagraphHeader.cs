namespace SharedKernel;

public static class ParagraphHeader
{
    public static string Format(string law, string number, string title) =>
        title.Length == 0 ? $"{law} § {number}" : $"{law} § {number} {title}";
}