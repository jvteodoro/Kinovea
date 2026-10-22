namespace Kinovea.PortableGeometry;

/// <summary>A point in a two-dimensional coordinate space, measured in that space's units.</summary>
public readonly record struct Point2D(double X, double Y);

/// <summary>A two-dimensional extent measured in the corresponding coordinate space's units.</summary>
public readonly record struct Size2D(double Width, double Height);

/// <summary>A translation measured in the corresponding coordinate space's units.</summary>
public readonly record struct Vector2D(double X, double Y);

/// <summary>An axis-aligned rectangle measured in the corresponding coordinate space's units.</summary>
public readonly record struct Rect2D(double X, double Y, double Width, double Height);
