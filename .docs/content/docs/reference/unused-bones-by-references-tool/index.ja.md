---
title: UnusedBonesByReferencesTool
weight: 11
---

# UnusedBonesByReferencesTool

EditorOnlyなメッシュからしか参照がないボーンをEditorOnlyにします。

このコンポーネントは[Avatar Global Component](../../component-kind/avatar-global-components)であるため、アバターのルートに追加してください。

{{< hint warning >}}

このコンポーネントは非推奨です。代わりに[Trace and Optimize](../trace-and-optimize)の`使われていないObjectを自動的に削除する`を使用してください。
このコンポーネントの動作が改善されることはありません。

{{< /hint >}}

これはNarazakaさんの[UnusedBonesByReferencesTool][UnusedBonesByReferencesTool]を移植したものですが、ビルド時に実行します。

[UnusedBonesByReferencesTool]: https://narazaka.booth.pm/items/3831781

![component.png](component.png)
