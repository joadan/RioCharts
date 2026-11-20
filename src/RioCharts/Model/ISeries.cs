namespace RioCharts;

/// <summary>Common contract for all series types.</summary>
public interface ISeries
{
    /// <summary>Series identifier shown in the legend.</summary>
    string Name { get; }

    /// <summary>Show or hide the series. Settable so the legend can toggle it.</summary>
    bool Visible { get; set; }

    /// <summary>Draw order; lower values draw first.</summary>
    int RenderOrder { get; }

    /// <summary>Transparency, 0.0 to 1.0.</summary>
    double Opacity { get; }

    /// <summary>Selection state, toggled by clicking the series.</summary>
    bool IsSelected { get; set; }
}
