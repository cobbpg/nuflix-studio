public static class Constants
{
    public const int PaletteSize = 16;

    public const int ScreenWidth = 320;
    public const int ScreenHeight = 200;
    public const int AttributeWidth = ScreenWidth >> 3;
    public const int AttributeHeight = ScreenHeight >> 1;
    public const int SpriteWidth = 24;
    public const int SpriteBlockWidth = SpriteWidth >> 3;

    public const int BugBlockWidth = SpriteBlockWidth;
    public const int BugPatternWidth = SpriteWidth >> 1;
    public const int BugSprites = 2;
    public const int BugColorSlots = 4;
    public const int UnderlayBlockWidth = SpriteWidth >> 2;
    public const int UnderlayColumns = 6;

    public const int MidStartX = BugBlockWidth << 3;
    public const int MidEndX = MidStartX + (UnderlayColumns * UnderlayBlockWidth << 3);
}