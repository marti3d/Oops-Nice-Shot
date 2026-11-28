using System;

// Marks the first serialized field of an Inspector section.
[AttributeUsage(AttributeTargets.Field)]
public sealed class MiniGolfFoldoutAttribute : Attribute
{
    public string Title { get; }

    public MiniGolfFoldoutAttribute(string title)
    {
        Title = title;
    }
}
