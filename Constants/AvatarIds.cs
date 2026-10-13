namespace Kue.Api.Constants;

/// <summary>
/// The 10 selectable profile avatars. Each id maps to a static SVG served by
/// the web app from /public/avatars/&lt;id&gt;.svg — generated once from the
/// DiceBear "critters" style (CC0 1.0) by kue-web/scripts/generate-avatars.mjs.
/// Must stay in sync with AVATAR_IDS in kue-web/lib/avatars.ts.
/// </summary>
public static class AvatarIds
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "fox",
        "owl",
        "frog",
        "bee",
        "bunny",
        "panda",
        "koala",
        "raccoon",
        "hedgehog",
        "penguin",
    };
}
