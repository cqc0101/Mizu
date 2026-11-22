# Mizu 鱼形图标

用户提供的 fish.jpg 经内置 imagegen 工具提取透明背景，保留为 mizu-source.png。
运行 pwsh ./scripts/build_icon.ps1 可重新生成 mizu.ico，包含 16、20、24、32、40、48、64、96、128、256 像素图层。
所有程序、窗口、托盘、快捷方式和安装包共用此 ICO。

处理提示词：

> Use case: background-extraction. Edit target: the supplied fish.jpg. Prepare this exact existing fish illustration for a Windows application icon. Remove ONLY the off-white background and its large empty margins; produce a genuinely transparent background. Preserve the original fish facing left, exact silhouette, navy thick outlines, eye and scale marks, and soft pale blue to aqua fill. Do not redraw, restyle, add shadows, lettering, a badge, or a background tile. Place the complete uncropped fish horizontally centered within a square canvas, occupying roughly 90% of the canvas width, with balanced transparent margins above and below. Keep crisp antialiased edges suitable for small system tray icons. Output a transparent PNG asset.
