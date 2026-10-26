namespace MusicManager.InterfaceAdapter;

/// <summary>Java's PropertyChangeEvent: which property changed (and, for these view models, the state it carries).</summary>
public sealed class PropertyChangeEvent
{
    public PropertyChangeEvent(object source, string propertyName, object oldValue, object newValue)
    {
        Source = source;
        PropertyName = propertyName;
        OldValue = oldValue;
        NewValue = newValue;
    }

    public object Source { get; }
    public string PropertyName { get; }
    public object OldValue { get; }
    public object NewValue { get; }
}

/// <summary>Java's PropertyChangeListener (a functional interface, so a delegate: lambdas work).</summary>
public delegate void PropertyChangeListener(PropertyChangeEvent evt);

/// <summary>
/// The ViewModel for our CA implementation. This class delegates work to a listener list for managing the property
/// change events (Java: PropertyChangeSupport). Listeners are called synchronously, on the thread that fires.
/// </summary>
/// <typeparam name="T">The type of state object contained in the model.</typeparam>
public class ViewModel<T>
{
    private readonly object _lock = new();
    private readonly List<PropertyChangeListener> _listeners = new();

    public ViewModel(string viewName)
    {
        ViewName = viewName;
    }

    public string ViewName { get; }

    public T State { get; set; }

    /// <summary>Fires a property changed event for the state of this ViewModel.</summary>
    public void FirePropertyChange() => FirePropertyChange("state");

    /// <summary>
    /// Fires a property changed event for the state of this ViewModel, which allows the user to specify a different
    /// propertyName (a class listening for several kinds of change tells them apart by it).
    /// </summary>
    public void FirePropertyChange(string propertyName)
    {
        PropertyChangeListener[] snapshot;
        lock (_lock)
        {
            snapshot = _listeners.ToArray(); // a listener may add another while it runs
        }
        var evt = new PropertyChangeEvent(this, propertyName, null, State);
        foreach (var listener in snapshot)
        {
            listener(evt);
        }
    }

    /// <summary>Adds a listener to this ViewModel (there is no way to remove one, as in the Java code).</summary>
    public void AddPropertyChangeListener(PropertyChangeListener listener)
    {
        lock (_lock)
        {
            _listeners.Add(listener);
        }
    }
}
