extends SceneTree

# Standalone native Godot SVG rasterization; does not import or modify shared resources.
# Run: Godot --headless --path game --script res://Ui/Tower/Art/verify.gd
func _initialize():
	var entries: Array[String] = []
	for folder in ["activities", "currencies", "symbols"]:
		for file in DirAccess.get_files_at("res://Ui/Tower/Art/" + folder):
			if file.ends_with(".svg"):
				entries.append(folder + "/" + file)
	entries.sort()
	var sheet = Image.create(384, entries.size() * 76, false, Image.FORMAT_RGBA8)
	sheet.fill(Color("0c1114"))
	for row in entries.size():
		var svg = FileAccess.get_file_as_string("res://Ui/Tower/Art/" + entries[row])
		var x = 8
		for size in [64, 32, 24, 16]:
			var raster = Image.new()
			assert(raster.load_svg_from_string(svg, float(size) / 32.0) == OK)
			assert(raster.get_width() == size and raster.get_height() == size)
			assert(not raster.get_used_rect().size == Vector2i.ZERO)
			sheet.blend_rect(raster, Rect2i(0, 0, size, size), Vector2i(x, row * 76 + (76-size)/2))
			x += size + 16
	assert(entries.size() == 24)
	assert(sheet.save_png("res://Ui/Tower/Art/size-check.png") == OK)
	print("TOWER ART: 24 SVG x 64/32/24/16 px OK; row order: ", entries)
	quit()
