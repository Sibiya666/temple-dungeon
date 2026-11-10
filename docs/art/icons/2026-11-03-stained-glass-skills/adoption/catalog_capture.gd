extends SceneTree

var output_dir := "C:/Users/sibiya666/Documents/project/dialoid/docs/art/icons/2026-11-03-stained-glass-skills/adoption"

func _initialize() -> void:
	call_deferred("run_capture")

func locate_item(item: TreeItem, fragment: String) -> TreeItem:
	if item.get_tooltip_text(0).contains(fragment):
		return item
	var child := item.get_first_child()
	while child != null:
		var found := locate_item(child, fragment)
		if found != null:
			return found
		child = child.get_next()
	return null

func load_reference(main: Node, fragment: String) -> bool:
	for tree in main.find_children("*", "Tree", true, false):
		if tree.get_root() == null:
			continue
		var found := locate_item(tree.get_root(), fragment)
		if found == null:
			continue
		found.select(0)
		tree.emit_signal("item_activated")
		return true
	return false

func capture(filename: String) -> void:
	for i in range(4):
		await process_frame
	await RenderingServer.frame_post_draw
	var error := root.get_texture().get_image().save_png(output_dir.path_join(filename))
	if error != OK:
		push_error("GLASS_CATALOG_FAILED: cannot save " + filename)
		quit(1)

func run_capture() -> void:
	var main: Node = load("res://Main.tscn").instantiate()
	root.add_child(main)
	for i in range(4):
		await process_frame
	if not load_reference(main, "sorcerer_cryomancer_ice_lance"):
		push_error("GLASS_CATALOG_FAILED: cryomancer reference missing")
		quit(1)
		return
	for i in range(4):
		await process_frame
	main.call("CloseAllWindows")
	main.call("OpenWindow", "skills")
	await capture("sorcerer-catalog.png")
	for id in ["fireball", "ice_lance", "spark"]:
		var button := main.find_child("Skill_" + id, true, false)
		if button == null:
			push_error("GLASS_CATALOG_FAILED: skill absent " + id)
			quit(1)
			return
		button.emit_signal("pressed")
		await capture("skill-" + id + ".png")
		main.call("CloseAllWindows")
		main.call("OpenWindow", "skills")
		# Return the catalog through the actual Back button when a detail is open.
		var back: Node = main.find_child("BackToSkills", true, false)
		if back != null:
			back.emit_signal("pressed")
		for i in range(4):
			await process_frame
	if not load_reference(main, "warrior_fire_cleave_crit"):
		push_error("GLASS_CATALOG_FAILED: warrior reference missing")
		quit(1)
		return
	for i in range(4):
		await process_frame
	main.call("CloseAllWindows")
	main.call("OpenWindow", "skills")
	await capture("warrior-catalog.png")
	print("GLASS_CATALOG_OK: actual sorcerer and warrior catalog/detail frames")
	quit(0)
