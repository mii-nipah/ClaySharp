# Building a Clay Renderer: Specifications & Best Practices

If you are reading this, you have successfully integrated Clay's layout algorithm. Clay has calculated the math, sized the elements, and resolved the Z-indexing. It has handed you a single, flat 1D array/list: `RenderCommandArray`.

Your job now is trivial: **Be a dumb painter.**

You do not need to know about UI hierarchies, padding, layouts, or flexbox math. You simply loop through the array and paint the shapes exactly where Clay tells you to.

However, translating UI concepts into low-level Graphics APIs (like OpenGL, DirectX, Vulkan, or frameworks like SDL/Raylib) comes with a few specific quirks. This guide outlines the standard implementation details and common "gotchas" you will face.

---

## 1. The Main Loop Structure
Your render function should look like a giant `switch` or `match` statement iterating over the array of render commands.

```text
function RenderClayUI(renderCommands):
    for each command in renderCommands:
        box = command.boundingBox // Contains Absolute X, Y, Width, Height

        switch command.type:
            case RECTANGLE:
                DrawRectangle(box, command.config)
            case TEXT:
                DrawText(box, command.config)
            case IMAGE:
                DrawImage(box, command.config)
            case BORDER:
                DrawBorders(box, command.config)
            case SCISSOR_START:
                EnableClippingMask(box)
            case SCISSOR_END:
                DisableClippingMask()
            case OVERLAY_COLOR_START:
                EnableTintShader(command.config.color)
            case OVERLAY_COLOR_END:
                DisableTintShader()
            case CUSTOM:
                HandleCustomData(box, command.customData)
```

---

## 2. Implementation Tips & "Gotchas"

### Tip 1: The Null-Terminator Trap (String Slices)
**The Problem:** Most graphics APIs (like SDL_ttf or Raylib) expect text to be passed as a standard C-string, which ends in a null-terminator (`\0`). Clay is highly optimized and **does not use null-terminated strings**. Instead, it passes text as a "Slice"—a pointer to a character array and an integer length. This allows Clay to handle text wrapping efficiently without allocating new memory for substrings.

**The Solution:** You must bridge this gap in your renderer. Do not allocate and free memory on every single frame just to append a `\0`. Instead, create a persistent, reusable buffer that resizes on demand.

```text
// Global or Persisted State
global string_buffer = ""

// Inside the TEXT render case:
text_slice = command.textData.stringContents
required_length = text_slice.length + 1

// Grow buffer if it's too small
if string_buffer.capacity < required_length:
    string_buffer.resize(required_length)

// Copy the exact characters from the slice
string_buffer.copy_bytes(from: text_slice.chars, length: text_slice.length)

// Append the null-terminator safely
string_buffer[text_slice.length] = '\0'

// Now safe to pass to standard font rendering APIs
DrawGraphicsText(font, string_buffer, box.x, box.y)
```

### Tip 2: "Frankensteining" Borders
**The Problem:** UI Borders can be incredibly complex. A single container might have a thick red top border, a thin blue bottom border, and rounded corners (`border-radius`). Most Graphics APIs only give you a basic `DrawRectangleLines()` function, which won't handle independent edge widths.

**The Solution:** You will likely need to "Frankenstein" the border together using primitive shapes.
1. Read the border widths from the command config (left, right, top, bottom).
2. Draw 4 independent, solid rectangles to represent the straight edges.
3. If the `cornerRadius` is greater than 0, use pie-slice/ring drawing functions (e.g., a custom OpenGL circle shader with an inner radius, or a framework's `DrawRing` function) to draw the 4 corners independently.

### Tip 3: Scissor Clipping (Scroll Views)
**The Problem:** When you have a scrolling list, the text and images inside it need to be visually cut off when they scroll outside the bounds of the parent container.

**The Solution:** Clay handles the math for scrolling, but relies on your renderer to handle the actual masking via the `SCISSOR_START` and `SCISSOR_END` commands.
*   **OpenGL/DirectX:** This translates directly to `glScissor(x, y, width, height)` and `glEnable(GL_SCISSOR_TEST)`.
*   **Important Gotcha:** Remember that some graphics APIs (like OpenGL) use a coordinate system that starts at the **Bottom-Left** of the screen, while UI engines (like Clay) start at the **Top-Left**. You will likely need to invert the Y-axis when applying the scissor mask: `(WindowHeight - box.y - box.height)`.

### Tip 4: Stateful Overlays (Tinting)
**The Problem:** Clay supports UI overlays (`OVERLAY_COLOR_START` / `END`). This is often used to tint a button when hovered, blending a transparent color over the top of backgrounds, images, and text seamlessly.

**The Solution:** Like scissoring, this is a stateful graphics command.
1. When you hit `START`, you should activate a custom shader or blend state.
2. In GLSL, this looks like: `mix(originalTextureColor.rgb, overlayColor.rgb, overlayColor.a);`
3. Everything drawn after this command in your loop will be processed through this shader.
4. When you hit `END`, revert to your default UI shader.

### Tip 5: The "Escape Hatch" (Custom Rendering)
**The Problem:** How do you draw a 3D spinning sword inside a 2D UI inventory slot? Or a complex, animated gradient background? Clay natively only outputs Rectangles, Images, Borders, and Text.

**The Solution:** The `CUSTOM` command.
During layout declaration, Clay allows you to pass an opaque pointer/reference (`customData`) to an element. Clay will blindly pass this exact reference back to you in the render loop, along with the calculated 2D `boundingBox`.

You can unpack this data to trigger engine-specific rendering:
```text
case CUSTOM:
    // 1. Unpack your custom data from the generic reference
    my_custom_data = cast_to_my_data_type(command.customData)

    if my_custom_data.type == 3D_MODEL:
        // 2. Use Clay's 2D bounding box to find the UI center
        center_x = box.x + (box.width / 2)
        center_y = box.y + (box.height / 2)

        // 3. Project the 2D UI coordinates into your 3D world space
        world_position = ScreenToWorldSpace(center_x, center_y)

        // 4. Render the 3D model exactly where the UI container is!
        Draw3DModel(my_custom_data.model, world_position)
```

---
**Final Note:** Because the render loop is entirely decoupled from the layout math, you are completely free to optimize it. You can batch draw calls, construct vertex buffers, or multi-thread this render loop as heavily as your specific game engine requires without ever touching Clay's internal logic.
