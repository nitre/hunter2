@tool
extends EditorPlugin
# On a macOS export: the repository's game_data folder is copied into the app (Contents/PlugIns/game_data) before
# Godot signs the app, so the signature covers it. Beside the app it was lost whenever macOS ran the app from its
# hidden temporary copy, which holds the app alone. Main.cs looks there first on macOS.

var export_plugin: EditorExportPlugin


func _enter_tree() -> void:
	export_plugin = MacGameData.new()
	add_export_plugin(export_plugin)


func _exit_tree() -> void:
	remove_export_plugin(export_plugin)
	export_plugin = null


class MacGameData extends EditorExportPlugin:
	func _get_name() -> String:
		return "MacGameData"

	func _export_begin(features: PackedStringArray, _is_debug: bool, _path: String, _flags: int) -> void:
		if features.has("macos"):
			# Contents/Resources, not PlugIns: the signature takes everything under PlugIns for code (a nested bundle,
			# here with no signature of its own), while under Resources each file is listed and hashed as plain data
			add_shared_object(ProjectSettings.globalize_path("res://../game_data"), PackedStringArray(), "Contents/Resources")
