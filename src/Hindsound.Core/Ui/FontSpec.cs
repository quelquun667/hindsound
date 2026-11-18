using Hindsound.Ui.Window;

namespace Hindsound.Ui;

/// <summary>One font of the interface at one size, in physical pixels (the em size, as GDI's negative height was).</summary>
internal readonly record struct FontSpec(Face Face, float Size);
