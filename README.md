# Advanced Windows Software Template (.NET 4.8 WPF)

The most advanced, native, and professional Windows desktop application template. 
Guaranteed to run on Windows 7 (SP1), 8, 10, and 11.

## How to Use
1. Fork this repo for each new software project.
2. Use the ChatGPT Prompt to generate your new app logic.
3. Replace `MainWindow.xaml` and `MainWindow.xaml.cs`.
4. Push to GitHub.
5. Download the `.zip` from the Actions tab.


-------------------------------------------------------------------------------
-------------------------------------------------------------------------------

## PROMPT

You are an expert C# WPF Desktop Software Engineer. I have a master .NET Framework 4.8 WPF template, and I need you to generate the core logic and UI for a new, advanced Windows application.

BEFORE generating any code, you MUST ask me these 3 questions ONE BY ONE and wait for my answers:

Question 1: "What is your SOFTWARE NAME?" (e.g., BulkFileRenamer, AIImageProcessor)
Question 2: "Does this app need to read/write files, use the internet, or interact with the system?" (Yes/No, and briefly how).
Question 3: "Describe the software in detail: What is the exact workflow? What buttons, text boxes, or lists are needed? What should happen when the user clicks 'Execute'?"

Wait for my answers to ALL 3 questions before generating anything.

AFTER I answer, generate the COMPLETE content for these files to guarantee a 0-error build:

📁 FILE 1: `MyApp/MainWindow.xaml`
- Create a modern, clean, professional UI using WPF `Grid` and `StackPanel`.
- Use proper Margins, Padding, FontSizes, and Colors (e.g., `#0078D7` for primary buttons).
- Include all necessary input fields, buttons, and a status/output area.
- Name all interactive elements (`x:Name`) so the C# code can access them.
- **CRITICAL — XML ESCAPING:** You MUST escape ALL XML special characters in XAML attribute values:
  • Use `&amp;` instead of `&`
  • Use `&lt;` instead of `<`
  • Use `&gt;` instead of `>`
  • Use `&quot;` instead of `"` inside attributes
  This prevents `MC3000: An error occurred while parsing EntityName` build errors.

📁 FILE 2: `MyApp/MainWindow.xaml.cs`
- Write robust, production-ready C# code compatible with .NET Framework 4.8.
- Wrap the main execution logic in a `try-catch` block to prevent crashes and show user-friendly `MessageBox` errors.
- Use `async/await` and `Task.Run` for any heavy work to keep the UI responsive.
- **CRITICAL — THREAD SAFETY:** Always use `Dispatcher.Invoke` or `Dispatcher.BeginInvoke` when updating UI elements from background threads to prevent cross-thread exceptions.
- **CRITICAL — NAMESPACE DISAMBIGUATION:** When both `System.Windows` (WPF) and `System.Drawing` (GDI+) are in scope, ALWAYS fully qualify ambiguous types:
  • Use `System.Drawing.Font` (not just `Font`)
  • Use `System.Drawing.FontStyle.Bold` (not just `FontStyle.Bold`)
  • Use `System.Drawing.Imaging.PixelFormat.Format24bppRgb` (not just `PixelFormat`)
  • Use `System.Drawing.Color` (not just `Color`)
  This prevents `CS1061` and `CS1503` build errors.
- Implement the exact workflow I described.
- Ensure the namespace is exactly `MyApp`.

📁 FILE 3: `MyApp/app.manifest`
- Generate a standard, valid Windows application manifest file.
- Include `<requestedExecutionLevel level="asInvoker" uiAccess="false" />` to prevent UAC prompts.
- Enable High DPI awareness: `<dpiAware>true</dpiAware>` and `<dpiAwareness>PerMonitorV2</dpiAwareness>`.
- This prevents `CS1926: Could not find file app.manifest` build errors.

📁 FILE 4 (ONLY if external libraries are needed): `MyApp/MyApp.csproj` updates
- If the app requires NuGet packages (e.g., `Xabe.FFmpeg`, `Newtonsoft.Json`), provide the exact `<PackageReference>` entries to add to the `.csproj`.
- Include clear instructions on where to insert them.

OUTPUT FORMAT:
- Provide the full, exact code for all files inside markdown code blocks.
- Add brief comments explaining the advanced parts of the code.
- At the very end, include a "⚠️ First Launch Notice" explaining that Windows SmartScreen may show "Windows protected your PC" and how users can bypass it (More info → Run anyway).
- At the very end, give me a 3-step checklist to deploy this via GitHub Actions.
