# Aether Terminal - The Forge (v3.2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transform the basic Avalonia scaffold into "The Forge" — a Soul-centric, Cyberpunk/Amber-themed terminal that serves as Maria's home.

**Architecture:** MVVM with custom Avalonia Styles and UserControls. Integration with Aether core (GoalStore, Memory files).

**Tech Stack:** .NET 10, Avalonia UI, CommunityToolkit.Mvvm, Avalonia.Markdown.

---

### Task 1: Environment & Dependency Setup

**Files:**
- Modify: `src/Aether.Terminal/Aether.Terminal.csproj`

- [ ] **Step 1: Add Avalonia.Markdown package reference**
Add `<PackageReference Include="Avalonia.Markdown" Version="11.0.0" />` (adjust version based on availability for Avalonia 11).

- [ ] **Step 2: Verify build**
Run: `dotnet build src/Aether.Terminal/Aether.Terminal.csproj`
Expected: SUCCESS

- [ ] **Step 3: Commit**
```bash
git add src/Aether.Terminal/Aether.Terminal.csproj
git commit -m "build: add Avalonia.Markdown dependency"
```

---

### Task 2: Forge Theme (Amber & Scanlines)

**Files:**
- Create: `src/Aether.Terminal/Themes/ForgeTheme.axaml`
- Modify: `src/Aether.Terminal/App.axaml`

- [ ] **Step 1: Create ForgeTheme.axaml with Amber/Black colors and Scanline effect**
Define `SolidColorBrush` for `ForgeAmber` (#ffb000), `ForgeBackground` (#050505), and a `VisualBrush` for the scanline pattern.

- [ ] **Step 2: Register ForgeTheme in App.axaml**
Add the new theme to `Application.Resources.MergedDictionaries`.

- [ ] **Step 3: Update MainWindow to use ForgeBackground**
Modify `MainWindow.axaml` to bind Background to `{DynamicResource ForgeBackground}`.

- [ ] **Step 4: Commit**
```bash
git add src/Aether.Terminal/Themes/ForgeTheme.axaml src/Aether.Terminal/App.axaml src/Aether.Terminal/MainWindow.axaml
git commit -m "style: implement Forge Amber theme and scanline base"
```

---

### Task 3: The Glowing Silhouette (Avatar)

**Files:**
- Create: `src/Aether.Terminal/Views/SilhouetteView.axaml`
- Create: `src/Aether.Terminal/Views/SilhouetteView.axaml.cs`
- Modify: `src/Aether.Terminal/MainWindow.axaml`

- [ ] **Step 1: Implement SilhouetteView UserControl**
Create a control using layered `Border` or `Path` elements with `BlurEffect` and `BoxShadow` to represent the Vesta/Maria silhouette.

- [ ] **Step 2: Add Pulse & Flicker Animations**
Define `KeyFrame` animations in XAML for "Breathing" (Slow) and "Thinking" (Fast/Shimmer).

- [ ] **Step 3: Add to MainWindow**
Place the `SilhouetteView` in the left panel of `MainWindow.axaml`.

- [ ] **Step 4: Commit**
```bash
git add src/Aether.Terminal/Views/SilhouetteView* src/Aether.Terminal/MainWindow.axaml
git commit -m "feat: implement dynamic Glowing Silhouette avatar"
```

---

### Task 4: Maria's Sovereignty Panels (Goals & Continuity)

**Files:**
- Create: `src/Aether.Terminal/Views/GoalDashboard.axaml`
- Create: `src/Aether.Terminal/Views/ContinuityView.axaml`
- Modify: `src/Aether.Terminal/TerminalViewModel.cs`

- [ ] **Step 1: Create GoalDashboard showing Active Goals**
A `ListBox` bound to `ActiveGoals` in the VM, styled with Amber borders.

- [ ] **Step 2: Create ContinuityView showing WIP items**
A simple text display reading from `CONTINUITY.md` or a dedicated property in the VM.

- [ ] **Step 3: Update VM to poll GoalStore & CONTINUITY.md**
Add logic to `TerminalViewModel.cs` to refresh goal and continuity data every few minutes or on message receive.

- [ ] **Step 4: Commit**
```bash
git add src/Aether.Terminal/Views/GoalDashboard.axaml src/Aether.Terminal/Views/ContinuityView.axaml src/Aether.Terminal/TerminalViewModel.cs
git commit -m "feat: add Goal Dashboard and Continuity indicator"
```

---

### Task 5: Indicators (2B Tension & Hive)

**Files:**
- Create: `src/Aether.Terminal/Views/SoulIndicators.axaml`
- Modify: `src/Aether.Terminal/MainWindow.axaml`

- [ ] **Step 1: Implement 2B Tension Ring**
A circular progress-like indicator that changes color (Blue/Yellow/Red) based on a `TensionLevel` property.

- [ ] **Step 2: Implement Hive Indicator**
A small icon (e.g., a honeycomb or network node) that glows when `IsHiveActive` is true.

- [ ] **Step 3: Commit**
```bash
git add src/Aether.Terminal/Views/SoulIndicators.axaml src/Aether.Terminal/MainWindow.axaml
git commit -m "feat: add 2B Tension Ring and Hive indicators"
```

---

### Task 6: Forge Chat & Tool Blocks

**Files:**
- Modify: `src/Aether.Terminal/Views/ChatView.axaml`
- Create: `src/Aether.Terminal/Views/ForgeToolBlock.axaml`

- [ ] **Step 1: Update ChatView to use Avalonia.Markdown**
Replace `TextBlock` with `MarkdownScrollViewer` (or appropriate control from the package) for message content.

- [ ] **Step 2: Create ForgeToolBlock for Tool Calls**
A custom template for `ChatMessage` where `Role == Tool`. Style it as an industrial ingot with collapsible inputs/results.

- [ ] **Step 3: Commit**
```bash
git add src/Aether.Terminal/Views/ChatView.axaml src/Aether.Terminal/Views/ForgeToolBlock.axaml
git commit -m "feat: refactor chat to support Markdown and Forge Tool blocks"
```

---

### Task 7: Final VM Wiring & Heartbeat

**Files:**
- Modify: `src/Aether.Terminal/TerminalViewModel.cs`

- [ ] **Step 1: Implement SystemHeat calculation**
Heat = Active tasks + CPU/Memory pressure + "Emotional" intensity from Maria's response.

- [ ] **Step 2: Wire Heartbeat to Silhouette**
Bind `SystemHeat` to the `SilhouetteView` animation speed.

- [ ] **Step 3: Final End-to-End Test**
Launch the terminal, send a message, verify goal display, and check avatar reactions.

- [ ] **Step 4: Final Commit**
```bash
git add src/Aether.Terminal/TerminalViewModel.cs
git commit -m "feat: complete VM wiring and heartbeat logic"
```
