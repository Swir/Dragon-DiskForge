# Dragon DiskForge — Dragon Visual System

Dragon DiskForge must look like Dragon DiskForge before the user reads its name. The visual identity should feel powerful, technical and premium rather than fantasy-cartoonish.

## Design direction

**Theme:** forged metal + obsidian + controlled heat.

The UI should combine Windows 11/Fluent usability with a distinctive Dragon layer:

- dark obsidian/charcoal surfaces
- molten ember accents for primary actions
- deep crimson as a secondary warning/energy accent
- restrained glow around active/mounted states
- subtle scale geometry in backgrounds and separators
- forged-metal borders/details
- custom dragon-head/sigil mark
- motion inspired by breathing/fire, used sparingly

## Core palette

These are design tokens, not hard-coded component colors:

- `Dragon.Obsidian` — near-black application background
- `Dragon.Charcoal` — primary cards/panels
- `Dragon.Iron` — secondary surfaces/borders
- `Dragon.Ember` — primary orange/amber action accent
- `Dragon.Molten` — brighter active/hover accent
- `Dragon.Crimson` — secondary energy/danger accent
- `Dragon.Ash` — muted text/dividers
- `Dragon.Bone` — high-contrast primary text

Light theme must translate the identity rather than simply invert colors.

## Signature elements

### Dragon sigil
A custom vector dragon-head/sigil becomes the app icon, splash mark, About mark and selected branded empty-state element.

### Scale pattern
A low-contrast geometric scale pattern may appear in the Home hero area, empty drop zone and splash background. It must never interfere with text or look like wallpaper pasted over every screen.

### Ember state
Primary actions, mounted images and successful active operations can use a restrained ember glow. The glow must not replace standard focus/accessibility indicators.

### Forge line
Separators/progress visuals may use a thin forged/heat-line treatment: dark metal at rest, ember when active.

## Main shell

The shell should include:

- branded Dragon DiskForge header
- custom dragon sigil
- Home
- Images
- Mounted
- Explorer
- Convert
- USB / Media
- Tools
- Settings

The navigation remains familiar and Windows-native while surfaces, icons, spacing and active states carry the Dragon identity.

## Home screen

The Home screen should prioritize one large drop target:

**DROP DISK IMAGE HERE**

Supported format summary sits underneath. A selected image transforms the area into an information card with:

- format
- size
- filesystem/partition data when known
- bootable state
- verification state
- Mount
- Explore
- Extract
- Verify
- Convert

Only actions backed by a working capability are enabled.

## Motion

Animations must be short and useful:

- ember pulse when an operation starts
- subtle breathing glow while an image is mounted
- scale/forge transition when a card expands
- smooth navigation and panel transitions

No continuous heavy particle effects and no animation that harms performance.

## Safety visuals

Dangerous actions such as writing a USB device use a separate clear destructive state. Dragon styling is allowed, but exact device identity, capacity and impact must be more visually important than decorative effects.

## Accessibility

- maintain readable contrast
- never encode state only by color
- preserve keyboard focus indicators
- provide reduced-motion behavior
- ensure scalable typography
- support high DPI
- prepare for screen-reader labels

## Rule

If a styling idea makes Dragon DiskForge look more dramatic but makes the application harder to understand, the styling loses. The goal is a professional Windows tool with a strong Dragon identity, not a game launcher.