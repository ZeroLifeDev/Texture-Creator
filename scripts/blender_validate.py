import bpy
import math
import os
import sys
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
texture_dir, blend_path, render_path = map(os.path.abspath, args[:3])
displacement_strength = float(args[3]) if len(args) > 3 else 0.0
prefix = "TEST-INPUT-uv-layout-cube_"

bpy.ops.wm.read_factory_settings(use_empty=True)

verts = []
faces = []
uv_rects = []

def add_face(points, rect):
    start = len(verts)
    verts.extend(points)
    faces.append((start, start + 1, start + 2, start + 3))
    x1, y1, x2, y2 = rect
    # Source image coordinates are top-left; Blender UV coordinates are bottom-left.
    uv_rects.append(((x1, 1024-y2), (x2, 1024-y2), (x2, 1024-y1), (x1, 1024-y1)))

add_face(((-1,-1,-1),(1,-1,-1),(1,-1,1),(-1,-1,1)), (256,384,448,576)) # front
add_face(((1,-1,-1),(1,1,-1),(1,1,1),(1,-1,1)), (448,384,640,576))     # right
add_face(((1,1,-1),(-1,1,-1),(-1,1,1),(1,1,1)), (640,384,816,576))     # back
add_face(((-1,1,-1),(-1,-1,-1),(-1,-1,1),(-1,1,1)), (80,384,256,576)) # left
add_face(((-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)), (256,192,448,384))     # top
add_face(((-1,1,-1),(1,1,-1),(1,-1,-1),(-1,-1,-1)), (256,576,448,768))# bottom

mesh = bpy.data.meshes.new("ValidatedCubeMesh")
mesh.from_pydata(verts, [], faces)
mesh.update()
uv_layer = mesh.uv_layers.new(name="UVMap")
for poly, rect in zip(mesh.polygons, uv_rects):
    for loop_index, uv in zip(poly.loop_indices, rect):
        uv_layer.data[loop_index].uv = (uv[0] / 1024.0, uv[1] / 1024.0)

cube = bpy.data.objects.new("PBR Reference Forge Cube", mesh)
bpy.context.collection.objects.link(cube)

mat = bpy.data.materials.new("Generated PBR Material")
mat.use_nodes = True
nodes = mat.node_tree.nodes
links = mat.node_tree.links
nodes.clear()
output = nodes.new("ShaderNodeOutputMaterial")
output.location = (900, 0)
bsdf = nodes.new("ShaderNodeBsdfPrincipled")
bsdf.location = (620, 0)
bsdf.inputs["IOR"].default_value = 1.5
links.new(bsdf.outputs["BSDF"], output.inputs["Surface"])

def image_node(label, suffix, y, colorspace):
    node = nodes.new("ShaderNodeTexImage")
    node.label = label
    node.name = label
    node.location = (-650, y)
    node.image = bpy.data.images.load(os.path.join(texture_dir, prefix + suffix + ".png"), check_existing=False)
    node.image.colorspace_settings.name = colorspace
    node.interpolation = 'Linear'
    node.extension = 'EXTEND'
    return node

albedo = image_node("Generated Albedo", "Albedo", 300, "sRGB")
roughness = image_node("Generated Roughness", "Roughness", 80, "Non-Color")
metalness = image_node("Generated Metalness", "Metalness", -120, "Non-Color")
normal_tex = image_node("Generated Normal", "Normal", -340, "Non-Color")
height_tex = image_node("Generated Displacement", "Displacement", -560, "Non-Color")
links.new(albedo.outputs["Color"], bsdf.inputs["Base Color"])
links.new(roughness.outputs["Color"], bsdf.inputs["Roughness"])
links.new(metalness.outputs["Color"], bsdf.inputs["Metallic"])

normal_map = nodes.new("ShaderNodeNormalMap")
normal_map.location = (120, -280)
normal_map.inputs["Strength"].default_value = 0.7
links.new(normal_tex.outputs["Color"], normal_map.inputs["Color"])
bump = nodes.new("ShaderNodeBump")
bump.location = (380, -220)
bump.inputs["Strength"].default_value = 0.2
bump.inputs["Distance"].default_value = 0.08
links.new(height_tex.outputs["Color"], bump.inputs["Height"])
links.new(normal_map.outputs["Normal"], bump.inputs["Normal"])
links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
cube.data.materials.append(mat)

if displacement_strength > 0:
    subdivision = cube.modifiers.new("Dense geometry for displacement", "SUBSURF")
    subdivision.subdivision_type = "SIMPLE"
    subdivision.levels = 6
    subdivision.render_levels = 6
    displacement_image = bpy.data.images.load(
        os.path.join(texture_dir, prefix + "Displacement.png"), check_existing=False
    )
    displacement_image.colorspace_settings.name = "Non-Color"
    displacement_texture = bpy.data.textures.new("Generated displacement texture", type="IMAGE")
    displacement_texture.image = displacement_image
    displacement = cube.modifiers.new("Serious generated displacement", "DISPLACE")
    displacement.texture = displacement_texture
    displacement.texture_coords = "UV"
    displacement.uv_layer = "UVMap"
    displacement.mid_level = 0.5
    displacement.strength = displacement_strength

bevel = cube.modifiers.new("Small edge bevel", "BEVEL")
bevel.width = 0.06
bevel.segments = 3

bpy.ops.mesh.primitive_plane_add(size=20, location=(0, 0, -1.08))
ground = bpy.context.object
ground_mat = bpy.data.materials.new("Studio Floor")
ground_mat.diffuse_color = (0.055, 0.065, 0.08, 1)
ground.data.materials.append(ground_mat)

def add_area(name, location, energy, size, color):
    data = bpy.data.lights.new(name, "AREA")
    data.energy = energy
    data.shape = "DISK"
    data.size = size
    data.color = color
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.location = location
    direction = Vector((0,0,0)) - obj.location
    obj.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()

add_area("Key", (4,-4,6), 900, 4.0, (1.0,0.88,0.72))
add_area("Fill", (-4,-1,3), 650, 3.0, (0.55,0.72,1.0))
add_area("Rim", (2,4,5), 800, 3.0, (0.72,0.86,1.0))

camera_data = bpy.data.cameras.new("Camera")
camera = bpy.data.objects.new("Camera", camera_data)
bpy.context.collection.objects.link(camera)
camera.location = (4.4, -5.6, 3.7)
camera.rotation_euler = (Vector((0,0,0)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
camera.data.lens = 52
bpy.context.scene.camera = camera

world = bpy.data.worlds.new("Studio World")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.012,0.016,0.024,1)
world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.28
bpy.context.scene.world = world

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 768
scene.render.resolution_y = 768
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.filepath = render_path
scene.render.film_transparent = False
scene.render.image_settings.color_mode = "RGBA"
scene.view_settings.look = "AgX - Medium High Contrast"

bpy.ops.wm.save_as_mainfile(filepath=blend_path)
bpy.ops.render.render(write_still=True)
print("BLEND_FILE=" + blend_path)
print("RENDER_FILE=" + render_path)
