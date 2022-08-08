<p align="center">
  <img src="public/logo.png" alt="Backport">
</p>

# Setup

This guide isn't in-depth. Basically, Backport is used to provide an in-game console. This console is based on the Risa language. The setup consists of
creating multiple objects and assigning some components. After this, you can use this console. You may customize the console appearance however you want.
However, to make your life easier, a default configuration is described below, and default sprites are included in this repository.

You must have [risa-sharp](https://github.com/exom-dev/risa-sharp) in your Unity project, and also a [C99 Risa](https://github.com/exom-dev/risa) DLL.
Note that they are **included** in all Backport releases on this repo, so you can directly use those.

Afterwards, create an object structure like this:

<p align="center">
  <img src="public/setup_structure.png" alt="Backport object structure">
</p>

See below how each object looks. Some image components have sprites attached to them, and you'll need them. You can find those sprites [here](https://github.com/deprimus/Backport/tree/master/public/assets).

<p align="center">
  <img src="public/setup_backport.png" alt="Backport">
</p>

<p align="center">
  <img src="public/setup_console_canvas.png" alt="Backport console canvas">
</p>

<p align="center">
  <img src="public/setup_background.png" alt="Backport background">
</p>

<p align="center">
  <img src="public/setup_output_container.png" alt="Backport output container">
</p>

<p align="center">
  <img src="public/setup_output.png" alt="Backport output">
</p>

<p align="center">
  <img src="public/setup_output_text_area.png" alt="Backport output text area">
</p>

<p align="center">
  <img src="public/setup_output_placeholder.png" alt="Backport output placeholder">
</p>

<p align="center">
  <img src="public/setup_output_text.png" alt="Backport output text">
</p>

<p align="center">
  <img src="public/setup_scrollbar.png" alt="Backport output scrollbar">
</p>

<p align="center">
  <img src="public/setup_sliding_area.png" alt="Backport sliding area">
</p>

<p align="center">
  <img src="public/setup_handle.png" alt="Backport handle">
</p>

<p align="center">
  <img src="public/setup_input_container.png" alt="Backport input container">
</p>

<p align="center">
  <img src="public/setup_input_prefix.png" alt="Backport input prefix">
</p>

<p align="center">
  <img src="public/setup_input.png" alt="Backport input">
</p>

<p align="center">
  <img src="public/setup_input_text_area.png" alt="Backport input text area">
</p>

<p align="center">
  <img src="public/setup_input_placeholder.png" alt="Backport input placeholder">
</p>

<p align="center">
  <img src="public/setup_input_text.png" alt="Backport input text">
</p>

Disable `Console Canvas`, and and make a prefab out of the root object. Make sure this object is present in every scene.

Implement your own commands in `BackportCommands.cs` as native Risa functions, and you're all set.

You now have a fully-functional in-game console. Press `~` to show/hide it.