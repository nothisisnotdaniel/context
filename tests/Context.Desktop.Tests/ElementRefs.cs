using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace Context.Tests;

// 491: bUnit writes an element's blazor:elementReference attribute only into the render that created the element; a
// later render (a load the page started by itself) leaves it empty, so a test that read the id from the markup failed
// under load. These helpers read the id from the component's private ElementReference field instead, on the renderer's
// dispatcher, where the field is written.
internal static class ElementRefs
{
    private static readonly Regex Segment = new(@"^(\w+)(?:\[(\^?)(\d+)\])?$", RegexOptions.CultureInvariant);

    /// <summary>The id of the element reference at <paramref name="path"/> in the first <typeparamref name="TComponent"/>
    /// under <paramref name="cut"/> (or <paramref name="cut"/> itself): a private field (<c>"cancelButton"</c>), an item of
    /// an array or list field (<c>"items[0]"</c>, <c>"items[^1]"</c>), or a member of such an item
    /// (<c>"rows[1].AcceptButton"</c>). Fails when the path names nothing or the id is empty.</summary>
    public static string Id<TComponent, TRoot>(IRenderedComponent<TRoot> cut, string path)
        where TComponent : IComponent
        where TRoot : class, IComponent
    {
        string id = BoundedWait.Result(
            cut.InvokeAsync(() =>
            {
                object component = cut.Instance is TComponent self ? self : cut.FindComponent<TComponent>().Instance;
                return Assert.IsType<ElementReference>(Resolve(component, path)).Id;
            }),
            BoundedWait.Dispatcher,
            $"reading {typeof(TComponent).Name}.{path} on the renderer's dispatcher");
        Assert.False(string.IsNullOrEmpty(id), $"{typeof(TComponent).Name}.{path} has no element reference yet");
        return id;
    }

    // Walks the path from the component, one field or property (and item) per segment.
    private static object? Resolve(object start, string path)
    {
        object? value = start;
        foreach (string part in path.Split('.'))
        {
            Match m = Segment.Match(part);
            Assert.True(m.Success, $"bad element reference path {path}");
            Assert.NotNull(value);
            value = Member(value, m.Groups[1].Value);
            if (m.Groups[3].Success)
            {
                IList list = Assert.IsAssignableFrom<IList>(value);
                int at = int.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                value = list[m.Groups[2].Value == "^" ? list.Count - at : at];
            }
        }

        return value;
    }

    private static object? Member(object owner, string name)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        Type type = owner.GetType();
        FieldInfo? field = type.GetField(name, Flags);
        if (field is not null)
        {
            return field.GetValue(owner);
        }

        PropertyInfo? property = type.GetProperty(name, Flags);
        Assert.True(property is not null, $"{type.Name} has no field or property {name}");
        return property.GetValue(owner);
    }
}
