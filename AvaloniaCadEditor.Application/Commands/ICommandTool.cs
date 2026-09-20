using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

public interface ICommandTool
{
    string Name { get; }
    
    /// <summary>
    /// Called when the user clicks on the canvas.
    /// </summary>
    void OnMouseDown(Sketch sketch, Point3D point);
    
    /// <summary>
    /// Called when the mouse moves. Useful for live preview.
    /// </summary>
    void OnMouseMove(Sketch sketch, Point3D point);
    
    /// <summary>
    /// Cancels the current command operation.
    /// </summary>
    void Cancel();
    
    /// <summary>
    /// Returns temporary entities to be drawn (e.g. a line being stretched).
    /// </summary>
    IEnumerable<Entity> GetPreviewEntities();

    /// <summary>
    /// Called when the user presses a key while the tool is active.
    /// </summary>
    void OnKeyDown(Sketch sketch, CadKey key);

    /// <summary>
    /// Called when the user types a numeric value and presses Enter.
    /// </summary>
    void OnValueEntered(Sketch sketch, double value);

    /// <summary>
    /// Returns the text to display in the Value Control Box (e.g. "Length: 50.0")
    /// </summary>
    string GetMeasurementText();
}
