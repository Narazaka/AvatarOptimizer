---
title: Merge Skinned Mesh
weight: 21
---

# Merge Skinned Mesh

Merges one or more SkinnedMeshRenderer and MeshRenderers into one SkinnedMeshRenderer.

This component is [Source Edit Skinned Mesh Component](../../component-kind/edit-skinned-mesh-components#source-component), so this should be added to a new GameObject which has a SkinnedMeshRenderer component without Mesh specified.

This component will merge mesh, bones, Materials, BlendShapes, and Bounds but other settings will not be modified.
Please edit SkinnedMeshRenderer component attached to same GameObject as MergeSkinnedMesh to set AnchorOverride or else.

This component is good for merging your cloth meshes and body meshes but not good for face meshes because BlendShape can cause performance impact.
BlendShape is a feature became heavier in proportion to the count of vertices and BlendShapes.
Merging SkinnedMesh increases vertices and face mesh usually have many BlendShapes.
That's why it's not good to merge face meshes.

In addition, because of same reasons, you should freeze & remove unchanging BlendShapes for body / cloth meshes.
You can freeze & remove BlendShape using [Freeze BlendShape](../freeze-blendshape) component.
Add this component to both/either merge source SkinnedMeshRenderer and/or merged SkinnedMeshRenderer to freeze & remove BlendShapes.

## Settings

![component.png](component.png)

### Skinned Renderers

The list of SkinnedMeshRenderers to be merged.

Drop to None element at the bottom to add renderer and reset to None to remove from the list.

## Static Renderers

The list of MeshRenderers (without mesh transform).

Those meshes are transformed to polygons weighted to one bone, the GameObject that MeshRenderer belongs to.

Drop to None element at the bottom to add renderer and reset to None to remove from the list.

## Remove Empty Renderer GameObject

If this checkbox is checked and the GameObject SkinnedMeshRenderer belongs to does not have
any other components than SkinnedMeshRenderer, the GameObject will be removed from Hierarchy.

## Merge Materials

If MergeSkinnedMesh component found some Materials used in multiple renderers, the Materials will be listed here.

Check `Merge` to merge those MaterialSlots (SubMeshes) into one MaterialSlot. This reduces DrawCalls.
