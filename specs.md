# How to Build a UI Layout Library

Building a UI layout library from scratch can seem like a monumental task, but the core complexity lies not in the math, but in how you structure the problem. By breaking the layout process down into a series of logical passes over a simple data structure, you can create a flexible, powerful system without getting bogged down in unfixable edge cases.

This guide outlines the theoretical foundation and the step-by-step algorithm required to build a system akin to Clay.

## 1. The Foundation: The Tree Structure

The first and most important insight is realizing how UI elements relate to one another.
*   **Positions are relative:** The position of any element is naturally relative to the container it is inside.
*   **Sizes are dependent:** The size of a container often depends on the combined sizes of the contents inside it.

This creates a recursive, bi-directional dependency. To model this, the entire UI must be represented in memory as a **Tree** data structure.

*   **Root Node:** The top-level container (e.g., the application window or a main dropdown). It has no parent.
*   **Child/Parent Nodes:** Containers inside the root. They have one parent and can have multiple children.
*   **Leaf Nodes:** The innermost elements (like text labels or icons) that contain no other elements.

> **Commentary: API Design - How to Actually Build the Tree in Code**
>
> While the tree is the underlying data structure, how do you allow a developer to easily construct this in an imperative language (like C, which Clay uses)?
>
> Nic explains that this is achieved through a declarative-looking API powered by two simple underlying functions: `OpenElement()` and `CloseElement()`.
>
> 1. When a developer calls `OpenElement()`, the system creates a new node in the tree, adds it as a child to the "currently active" element, and then sets this new node as the new "active" element.
> 2. When `CloseElement()` is called, the system simply updates the "currently active" element pointer back to its parent.
>
> By wrapping these functions in macros (e.g., `CLAY(...) { ... }`), you create a syntax that mimics opening and closing HTML tags. This allows developers to write clean, nested UI code that seamlessly builds the entire tree structure in a single pass in memory. Once the final root element is closed, the tree is complete and handed over to the layout algorithm below.

**Crucially, you must completely separate the act of *calculating the layout* from the act of *drawing the UI*.**

## 2. The Core Algorithm: A Multi-Pass Approach

You cannot calculate the layout of a complex UI in a single pass. Because a parent's size depends on its children (bottom-up), but a child might need to "grow" to fill remaining space in its parent (top-down), you must traverse the tree multiple times.

Furthermore, text elements complicate things: a text block's height depends on its width (due to wrapping). Therefore, you must calculate all widths *before* you can calculate heights.

The complete layout algorithm consists of these 7 sequential passes over the UI Tree:

### Pass 1 & 2: Sizing Widths

**Step 1: "Fit" Sizing Widths (Bottom-Up)**
You must traverse the tree in **Reverse Breadth-First Order** (starting at the leaf nodes and working up to the root).
*   For leaf nodes with fixed sizes or intrinsic sizes (like text's preferred width without wrapping), return their width.
*   For parent containers, calculate their width based on their layout direction:
    *   **Along the layout axis (e.g., a horizontal row):** The container's width is the **Sum** of all children's widths, PLUS padding (left + right), PLUS the total gap space between children.
        *   *Note: Calculate the gap space using the "Fence Post" formula: `(Number of Children - 1) * Gap Size`.*
    *   **Across the layout axis (e.g., a vertical column):** The container's width is the **Maximum** width among all its children, PLUS padding (left + right).

**Step 2: "Grow & Shrink" Sizing Widths (Top-Down)**
Now traverse the tree downwards. Look for containers that have a defined width but contain children set to "Grow" (take up remaining space) or elements that need to shrink to fit.
*   Calculate **Remaining Width**: `Parent Width - Padding - Total Size of Children - Total Gap Space`.
*   **Growing:** If there is positive remaining width, distribute it among the "Grow" children. To do this gracefully, iteratively expand the *smallest* grow elements until they match the next largest, and so on, until the space is used.
*   **Shrinking:** If the remaining width is negative (children overflow), shrink the children. Iteratively shrink the *largest* elements down until they reach the size of the next largest, until they fit or hit their absolute `min-width`.

### Pass 3: Wrapping Text

Now that every element has its final, absolute width calculated, you iterate through the tree specifically looking for Text elements. Based on their final assigned width, calculate where the text needs to wrap. This step establishes the final, required **height** for every text block.

### Pass 4 & 5: Sizing Heights

These passes are functionally identical to passes 1 and 2, but applied to the Y-axis.

**Step 4: "Fit" Sizing Heights (Bottom-Up)**
Traverse reverse breadth-first again.
*   For parents laid out **Top-to-Bottom**, the height is the **Sum** of child heights + padding + gaps.
*   For parents laid out **Left-to-Right**, the height is the **Maximum** child height + padding.
*(Note: Because of Pass 3, text elements now report their correct wrapped height).*

**Step 5: "Grow & Shrink" Sizing Heights (Top-Down)**
Traverse top-down again. Calculate remaining height in fixed-size containers and distribute it to "Grow" children (expanding the smallest first) or shrink children if they overflow.

### Pass 6: Calculating Positions and Alignment

At this stage, every single element in your tree has its final, correct `width` and `height`. The final layout step is to assign X and Y coordinates.

Traverse the tree Top-Down.
*   The Root node is placed at an origin (e.g., `0,0`).
*   For each parent, iterate through its children to assign their positions relative to the parent's top-left corner, taking the parent's padding into account.
*   Keep a running `offset` value.
    *   If laying out Left-to-Right, increment an `X offset` by the child's width + the gap size after placing each child.
    *   If laying out Top-to-Bottom, increment a `Y offset` by the child's height + the gap size.

**Handling Alignment (Center, Right, Bottom):**
Alignment is handled during this positioning pass. If a container is set to center its children:
1.  Calculate the **Remaining Space** along that axis (Parent Size - Padding - Total Content Size).
2.  Divide that remaining space by 2 to get the alignment offset.
3.  Add this alignment offset to the starting position before placing the first child.
    *(For Right/Bottom alignment, use the full remaining space as the offset).*

## 3. The Final Step: Drawing (Pass 7)

Once Pass 6 is complete, your Layout phase is entirely finished. The UI Tree now holds absolute sizes and relative positions for every element.

You can now pass this tree to your rendering engine. The renderer simply walks the tree one last time, taking the calculated `X`, `Y`, `Width`, and `Height` values, converting relative positions to absolute screen coordinates by adding parent positions to child positions, and drawing rectangles, text, and images to the screen.

---

## 4. From Theory to Production: How Clay Actually Implements This

While the 7-pass algorithm above is the exact conceptual framework underlying Clay, implementing this robustly in C requires moving away from naive object-oriented patterns to ensure high performance and memory safety. If you look under the hood at `clay.h`, you will notice a few key architectural differences:

**Data-Oriented Design Over Object Nodes**
In theory, a UI tree is made of "Nodes" containing lists of pointers to child "Nodes". In `clay.h`, there are no standard heap-allocated objects. Instead, the entire tree is stored in flat, contiguous memory arrays (e.g., `context->layoutElements`). The parent/child relationships are managed by storing integer indices rather than pointers. This allows Clay to destroy and rebuild the entire UI tree 60+ times a second with zero `malloc` calls or garbage collection overhead.

**Eliminating Recursion**
The theoretical guide suggests traversing the tree via Depth-First or Breadth-First searches. A naive implementation would use recursion. However, deep UI hierarchies can quickly cause stack overflows. `clay.h` strictly avoids recursion. Instead, it uses pre-allocated flat integer arrays (`bfsBuffer`, `dfsBuffer`, and `openLayoutElementStack`) to iterate through the tree safely, regardless of how deep the UI gets.

**Decoupled Rendering via "Render Commands"**
Pass 7 suggests passing the tree to your renderer. Clay takes this a step further to achieve total framework agnosticism. During the final positioning pass, rather than forcing the user to traverse the tree, Clay generates a 1D flat array of `Clay_RenderCommand` structs. The user's graphics engine simply loops over this flat array (which dictates bounding boxes, text strings, and colors) and draws them sequentially.

**Handling Real-World UI Complexities**
To be a production-ready library, Clay's layout passes weave in several advanced features not covered in the basic algorithm:
*   **Aspect Ratios:** During the X and Y sizing passes, Clay checks for elements configured with an `aspectRatio`. It calculates the missing dimension based on the resolved dimension and propagates that size back up the tree.
*   **Floating Elements (Z-Index):** Modals, tooltips, and dropdowns break standard layout flow. Clay extracts these elements into a `layoutElementTreeRoots` array, positions them absolutely during Pass 6, and sorts them by their `zIndex` so they are drawn on top.
*   **Scroll Containers:** Clay tracks internal scroll state across frames using a generational Hash Map. During positioning, it applies clipping commands (`SCISSOR_START` / `END`) and offsets child coordinates based on the user's scroll momentum.
*   **Animated Transitions:** Clay retains data from previous frames (like positions and colors) and automatically interpolates them using an "Ease Out" curve before emitting the final Render Commands.
