# Tutorial: build a component library

This tutorial builds a small component library the way the [layers](headless-libraries.md#layers)
suggest:

1. **`MyUi`**, a headless library: a `Disclosure` component with behaviour, ARIA and state, and no CSS.
2. **`MyUi.Tests`**, unit tests for it, run without a browser.
3. **`MyUi.Theme`**, a design system: an `Expander` that wraps `Disclosure`, and a stylesheet.
4. **An app** that uses both, first as projects and then as NuGet packages.

The finished code is in this repository: `src/NetWasm.UI.Primitives` (the `NwUi.Primitives`
package) is the headless library,
`samples/Primitives.Theme` the design system, `samples/Primitives` the app, and
`tests/NetWasm.UI.Tests/KitTests.cs` the tests.

You need the .NET 10 SDK or later. Browser apps also need NetWasm.Sdk; see
[Getting started](getting-started.md).

## 1. Create the library and its tests

```shell
dotnet new install NwUi.Templates
dotnet new netwasm-ui-lib -o MyUi
cd MyUi
dotnet test
```

The template creates:

```text
MyUi/
  MyUi.slnx
  MyUi/MyUi.csproj               the library
  MyUi/Disclosure.cs             a headless component
  MyUi.Tests/MyUi.Tests.csproj   xUnit + NwUi.Testing
  MyUi.Tests/DisclosureTests.cs
```

The library is a plain .NET project, not a NetWasm one:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <RootNamespace>MyUi</RootNamespace>
    <PackageId>MyUi</PackageId>
    <Version>0.1.0</Version>
    <Description>Headless NetWasm.UI components.</Description>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NwUi" Version="0.17.0" />
  </ItemGroup>
</Project>
```

- `netstandard2.1` makes one build that runs in browser apps (`net10.0-netwasm`), in
  [prerendering](deployment.md) on a server and in `net10.0` tests. See
  [Choosing a target framework](libraries.md#choosing-a-target-framework) for when to pick
  something else.
- `LangVersion` must be raised: `netstandard2.1` defaults to C# 8, and components use collection
  expressions, file-scoped namespaces and, in `.nwui` files, partial properties.
- `NwUi` brings the component API and the `.nwui` compiler. A library does not reference
  `NwUi.Browser`; the app does.

Without the template, create the same two projects by hand; the test project is shown in
[step 3](#3-test-it).

## 2. Write a headless component

A disclosure is a button that shows and hides a panel (the
[WAI-ARIA disclosure pattern](https://www.w3.org/WAI/ARIA/apg/patterns/disclosure/)). This is
`MyUi/Disclosure.cs` from the template:

```csharp
using System;
using NetWasm.UI;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace MyUi;

public sealed class Disclosure : Component
{
    public Signal<bool>? Open { get; set; }            // controlled: the caller owns the state
    public bool DefaultOpen { get; set; }              // uncontrolled: the initial state
    public Action<bool>? OnOpenChange { get; set; }    // the user toggled it
    public Prop<bool> Disabled { get; set; } = false;
    public Slot? Trigger { get; set; }                 // <template slot="Trigger">
    public Slot? ChildContent { get; set; }            // everything else between the tags

    protected override Node Build()
    {
        var open = Open ?? new Signal<bool>(DefaultOpen);
        var id = UseId("disclosure");
        string? State() => open.Value ? "open" : "closed";

        void Toggle()
        {
            var next = !open.Value;
            open.Value = next;
            OnOpenChange?.Invoke(next);
        }

        return Div(
            Attr("data-state", State),
            Splat(Attributes),
            Button(
                Type("button"),
                Id(id + "-trigger"),
                Attr("aria-controls", id + "-panel"),
                Attr("aria-expanded", () => open.Value ? "true" : "false"),
                Attr("data-state", State),
                Attr("disabled", () => Disabled.Value ? "" : null),
                Attr("data-disabled", () => Disabled.Value ? "" : null),
                OnClick(Toggle),
                Trigger is null ? Spread() : Trigger),
            Div(
                Id(id + "-panel"),
                Attr("role", "region"),
                Attr("aria-labelledby", id + "-trigger"),
                Attr("data-state", State),
                Attr("hidden", () => open.Value ? null : ""),
                ChildContent is null ? Spread() : ChildContent));
    }
}
```

What makes it a good headless component:

- **Written in C#.** A library can use `.nwui` files too, but C# keeps the structure and the ARIA
  in one place, and the component is just as easy to use from a template.
- **`UseId`** gives every instance its own ids, so `aria-controls` and `aria-labelledby` work
  when the page has many disclosures.
- **`Splat(Attributes)`** puts every attribute the caller writes on the tag (`class`, `id`,
  `data-*`, `@onclick`) on the root element. A caller's `class` is joined with the component's.
  See [Attribute splatting](templates.md#attribute-splatting).
- **State is published as attributes**: `aria-expanded` for assistive technology, and
  `data-state="open|closed"` and `data-disabled` for CSS. There are no classes and no styles.
- **Reactive attributes**: `Attr(name, () => …)` updates only that attribute when the signal
  changes, and `null` removes the attribute.
- **The platform does the rest**: `hidden` hides the panel, and a `<button>` is focusable and
  reacts to Enter and Space without any code.

### Controlled and uncontrolled

The props follow one convention, used by every component in a library:

| Prop | Meaning |
|---|---|
| `Open` (a `Signal<bool>?`) | Controlled. The caller owns the state, and `@bind-Open` binds it. |
| `DefaultOpen` | Uncontrolled. The component keeps its own signal, starting from this value. |
| `OnOpenChange` | Called when the user changes the state, in both modes. |

`var open = Open ?? new Signal<bool>(DefaultOpen);` is all the code it takes. Components with a
value use the same shape: `Value`, `DefaultValue` and `OnValueChange`. More conventions are in
[API conventions](headless-libraries.md#api-conventions).

## 3. Test it

`MyUi.Tests/MyUi.Tests.csproj` is a normal `net10.0` test project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NwUi.Testing" Version="0.17.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.1" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyUi\MyUi.csproj" />
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

[`NwUi.Testing`](testing.md) renders the component against an in-memory DOM. A test checks the
ARIA wiring and the behaviour:

```csharp
[Fact]
public void Clicking_the_trigger_opens_the_panel_and_reports_the_change()
{
    var changes = new List<bool>();
    using var r = TestRenderer.Render(new Disclosure
    {
        Trigger = new Slot(() => Text("More")),
        ChildContent = new Slot(() => P("Details")),
        OnOpenChange = changes.Add,
    });

    r.Click(r.Find("button"));

    Assert.Equal("true", r.Find("button").GetAttribute("aria-expanded"));
    Assert.Equal("open", r.Find("[role=region]").GetAttribute("data-state"));
    Assert.False(r.Find("[role=region]").HasAttribute("hidden"));
    Assert.Equal([true], changes);
}
```

Test what the component promises: the ARIA attributes, keyboard handling (`r.KeyDown(tab,
"ArrowRight")`), focus (`r.Focused`), the controlled mode, and that the caller's attributes end up
on the root. Test the look in a browser instead; see [Testing](testing.md#limits).

## 4. Use it in an app

Create an app next to the library:

```shell
cd ..
dotnet new netwasm-ui -o MyApp
```

Add the reference to the app, with `dotnet add MyApp reference MyUi/MyUi/MyUi.csproj` or by hand.
The app targets `net10.0-netwasm`, which uses a `netstandard2.1` library as any .NET 10 project
does:

```xml
<ItemGroup>
  <ProjectReference Include="..\MyUi\MyUi\MyUi.csproj" />
</ItemGroup>
```

Use the component in a page. Replace `MyApp/Pages/HomePage.nwui` with:

```razor
@using MyUi

<section>
    <h1>FAQ</h1>
    <Disclosure class="faq" @bind-Open="faqOpen" OnOpenChange="@(open => changes.Value++)">
        <template slot="Trigger">Do I need JavaScript?</template>
        <p>No. The component is C# compiled to WebAssembly.</p>
    </Disclosure>
    <p>@(faqOpen.Value ? "Open" : "Closed"), toggled @changes.Value times.</p>
</section>

@code {
    private readonly Signal<bool> faqOpen = new(false);
    private readonly Signal<int> changes = new(0);
}
```

- PascalCase attributes set props (`DefaultOpen="@true"`; plain text is a string, so other types
  need `@`), `@bind-Open` binds a signal, and
  lowercase attributes (`class`, `id`, `aria-*`) land on the root element.
- `<template slot="Trigger">` fills the named slot. The rest goes to `ChildContent`.

The app styles it through the published state:

```css
.faq { border: 1px solid #ccd; border-radius: .4rem; }
.faq > button { width: 100%; text-align: left; }
.faq > button[aria-expanded=true] { font-weight: bold; }
.faq > [role=region] { padding: .5rem .75rem; }
```

Run the app with `dotnet nwui-dev` from `MyApp`; it rebuilds when the library changes too.

## 5. Add a design system

A design system is a second library. Its components wrap the headless ones and add classes and
markup; a stylesheet styles them. Several design systems can share one headless library.

```shell
dotnet new classlib -o MyUi.Theme -f netstandard2.1
```

Delete `MyUi.Theme/Class1.cs` and replace `MyUi.Theme/MyUi.Theme.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <PackageId>MyUi.Theme</PackageId>
    <Version>0.1.0</Version>
    <Description>The MyUi design system.</Description>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NwUi" Version="0.17.0" />
    <ProjectReference Include="..\MyUi\MyUi\MyUi.csproj" />
    <!-- Served at _content/MyUi.Theme/theme.css and linked from the app's index.html. -->
    <NwUiAsset Include="theme.css" />
  </ItemGroup>
</Project>
```

Delete `Class1.cs` and add `MyUi.Theme/Expander.cs`:

```csharp
using System;
using MyUi;
using NetWasm.UI;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace MyUi.Theme;

public sealed class Expander : Component
{
    public string Title { get; set; } = "";
    public Signal<bool>? Open { get; set; }
    public bool DefaultOpen { get; set; }
    public Action<bool>? OnOpenChange { get; set; }
    public Slot? ChildContent { get; set; }

    protected override Node Build() => new Disclosure
    {
        Open = Open,
        DefaultOpen = DefaultOpen,
        OnOpenChange = OnOpenChange,
        Trigger = new Slot(() => new Content[]
        {
            Span(Class("ui-expander-icon"), Attr("aria-hidden", "true")),
            Span(Class("ui-expander-title"), Text(Title)),
        }),
        ChildContent = ChildContent,
        // The theme's class first; Splat joins it with the caller's.
        Attributes = [Class("ui-expander"), .. Attributes],
    }.Render();
}
```

- `Build()` returns the headless component's `Render()`, so the wrapper adds no element of its
  own.
- The wrapper passes the props on and keeps the same names, so moving between the headless and
  the themed component is a rename.
- It passes the caller's `Attributes` on after its own class. `<Expander class="wide">` renders
  `class="ui-expander wide"`.

Add `MyUi.Theme/theme.css`. Put the design decisions in custom properties, so an app can rebrand
without overriding selectors:

```css
:root {
  --ui-accent: #6a8cff;
  --ui-border: #ccd;
  --ui-radius: .4rem;
  --ui-duration: .2s;
}
@media (prefers-reduced-motion: reduce) { :root { --ui-duration: 0s; } }

.ui-expander { border: 1px solid var(--ui-border); border-radius: var(--ui-radius); }
.ui-expander > button { display: flex; gap: .5rem; width: 100%; padding: .5rem .75rem;
                        border: 0; background: none; font: inherit; text-align: left; cursor: pointer; }
.ui-expander > button[data-disabled] { cursor: not-allowed; opacity: .5; }
.ui-expander-icon::before { content: "\25B8"; display: inline-block; transition: rotate var(--ui-duration); }
.ui-expander[data-state=open] .ui-expander-icon::before { rotate: 90deg; }
.ui-expander > [role=region] { padding: 0 .75rem .75rem; }
```

Switch the app to the theme and use it. In `MyApp.csproj`, change the project reference to
`..\MyUi.Theme\MyUi.Theme.csproj` (the theme brings `MyUi` with it), and in the page add
`@using MyUi.Theme` and, inside the `<section>`:

```razor
<Expander Title="Shipping" DefaultOpen="@true">
    <p>Two to three working days.</p>
</Expander>
```

The app does not link the stylesheet itself. `NwUi.Browser` adds
`<link rel="stylesheet" href="/_content/MyUi.Theme/theme.css">` to `index.html` at build time,
before the app's own stylesheets, so the app wins when it overrides something:

```css
/* MyApp/wwwroot/app.css */
:root { --ui-accent: rebeccapurple; --ui-radius: 0; }
```

Add a test for the wrapper to `MyUi.Tests` (with a reference to `MyUi.Theme`): check that the
classes are joined and that the headless behaviour still works. `KitTests.cs` has one.

## 6. Pack and publish

```shell
dotnet pack MyUi -c Release -o packages
dotnet pack MyUi.Theme -c Release -o packages
```

Open the `.nupkg` files (they are zip files) and check them:

| Path in the package | What |
|---|---|
| `lib/netstandard2.1/MyUi.Theme.dll` | The components |
| `nwui-assets/theme.css` | The assets, from `NwUiAsset` |
| `build/MyUi.Theme.targets`, `buildTransitive/MyUi.Theme.targets` | Generated; tell the app which assets to serve |

`MyUi.Theme` depends on the `MyUi` package and on `NwUi`. Try the packages before you publish
them: add the folder as a package source for the app,

```shell
cd MyApp
dotnet new nugetconfig
dotnet nuget add source ../packages -n local --configfile nuget.config
```

and in `MyApp.csproj` replace the project reference with a package reference:

```xml
<PackageReference Include="MyUi.Theme" Version="0.1.0" />
```

NuGet caches packages by id and version, so bump `<Version>` (or clear the `myui.*` folders under
`~/.nuget/packages`) when you repack.

Run the app again. The published site should contain `_content/MyUi.Theme/theme.css`, and its
`index.html` should have the link. When everything works, push the packages:

```shell
dotnet nuget push packages/*.nupkg --source https://api.nuget.org/v3/index.json --api-key <key>
```

Read [Packaging and versioning](libraries.md#packaging-and-versioning) before the first release:
NetWasm.UI is 0.x, and a library should say which `NwUi` versions it supports.

## Next steps

- [Building a headless library](headless-libraries.md): API conventions, compound components
  such as tabs, patterns for dialogs and menus, and an accessibility checklist.
- `src/NetWasm.UI.Primitives/Tabs.cs`: a compound component whose parts share state through a context.
- [Templates](templates.md#events-on-components): events and generic components (`Select<TItem>`).
- [Libraries and packages](libraries.md#your-own-libraries): target frameworks, assets and
  JavaScript modules.
