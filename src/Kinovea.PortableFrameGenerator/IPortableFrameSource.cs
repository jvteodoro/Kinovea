namespace Kinovea.PortableFrameGenerator;

/// <summary>
/// Opens a media source and returns borrowed pixel frames for caller-supplied
/// timestamps. This contract does not define a frame rate or timestamp unit.
/// </summary>
/// <remarks>
/// The source owns pixel storage. Frames returned by <see cref="Generate"/>
/// borrow it and remain valid only until this source closes or replaces that
/// storage. Calls must be synchronous and externally serialized. A source
/// must preserve its own timestamp and previous-timestamp semantics. This
/// interface has no frame-rate or timestamp-unit field; the still-image shell
/// currently supplies its separately characterized 25 fps navigation profile.
/// </remarks>
public interface IPortableFrameSource : IDisposable
{
    bool IsOpen { get; }
    int Width { get; }
    int Height { get; }

    void Open(string path);
    PortableImageFrame Generate(long timestamp);
    void Close();
}
