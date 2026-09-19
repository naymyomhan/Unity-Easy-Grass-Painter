using System.Collections.Generic;
using UnityEngine;

namespace ModernGrassTool
{
    public class ModernCullingTreeNode
    {
        public Bounds bounds;
        public List<ModernCullingTreeNode> children = new List<ModernCullingTreeNode>();
        public List<int> grassIndices = new List<int>();

        public ModernCullingTreeNode(Bounds bounds, int depth)
        {
            this.bounds = bounds;
            children.Clear();

            if (depth > 0)
            {
                Vector3 size = bounds.size;
                Vector3 halfSize = size / 2.0f;
                Vector3 quarterSize = size / 4.0f;
                Vector3 center = bounds.center;

                // Subdivide in XZ plane (quadtree), keeping Y full or subdivided
                if (depth % 2 == 0)
                {
                    halfSize.y = bounds.size.y;
                    Bounds topLeft = new Bounds(new Vector3(center.x - quarterSize.x, center.y, center.z - quarterSize.z), halfSize);
                    Bounds topRight = new Bounds(new Vector3(center.x - quarterSize.x, center.y, center.z + quarterSize.z), halfSize);
                    Bounds bottomLeft = new Bounds(new Vector3(center.x + quarterSize.x, center.y, center.z - quarterSize.z), halfSize);
                    Bounds bottomRight = new Bounds(new Vector3(center.x + quarterSize.x, center.y, center.z + quarterSize.z), halfSize);

                    children.Add(new ModernCullingTreeNode(topLeft, depth - 1));
                    children.Add(new ModernCullingTreeNode(topRight, depth - 1));
                    children.Add(new ModernCullingTreeNode(bottomLeft, depth - 1));
                    children.Add(new ModernCullingTreeNode(bottomRight, depth - 1));
                }
                else
                {
                    // Octree subdivision for tall vertical terrains
                    Bounds tl1 = new Bounds(new Vector3(center.x - quarterSize.x, center.y - quarterSize.y, center.z - quarterSize.z), halfSize);
                    Bounds tr1 = new Bounds(new Vector3(center.x - quarterSize.x, center.y - quarterSize.y, center.z + quarterSize.z), halfSize);
                    Bounds bl1 = new Bounds(new Vector3(center.x + quarterSize.x, center.y - quarterSize.y, center.z - quarterSize.z), halfSize);
                    Bounds br1 = new Bounds(new Vector3(center.x + quarterSize.x, center.y - quarterSize.y, center.z + quarterSize.z), halfSize);

                    Bounds tl2 = new Bounds(new Vector3(center.x - quarterSize.x, center.y + quarterSize.y, center.z - quarterSize.z), halfSize);
                    Bounds tr2 = new Bounds(new Vector3(center.x - quarterSize.x, center.y + quarterSize.y, center.z + quarterSize.z), halfSize);
                    Bounds bl2 = new Bounds(new Vector3(center.x + quarterSize.x, center.y + quarterSize.y, center.z - quarterSize.z), halfSize);
                    Bounds br2 = new Bounds(new Vector3(center.x + quarterSize.x, center.y + quarterSize.y, center.z + quarterSize.z), halfSize);

                    children.Add(new ModernCullingTreeNode(tl1, depth - 1));
                    children.Add(new ModernCullingTreeNode(tr1, depth - 1));
                    children.Add(new ModernCullingTreeNode(bl1, depth - 1));
                    children.Add(new ModernCullingTreeNode(br1, depth - 1));
                    children.Add(new ModernCullingTreeNode(tl2, depth - 1));
                    children.Add(new ModernCullingTreeNode(tr2, depth - 1));
                    children.Add(new ModernCullingTreeNode(bl2, depth - 1));
                    children.Add(new ModernCullingTreeNode(br2, depth - 1));
                }
            }
        }

        public void RetrieveLeaves(Plane[] frustumPlanes, List<Bounds> visibleBoundsList, List<int> visibleIDList)
        {
            if (GeometryUtility.TestPlanesAABB(frustumPlanes, bounds))
            {
                if (children.Count == 0)
                {
                    if (grassIndices.Count > 0)
                    {
                        if (visibleBoundsList != null) visibleBoundsList.Add(bounds);
                        visibleIDList.AddRange(grassIndices);
                    }
                }
                else
                {
                    for (int i = 0; i < children.Count; i++)
                    {
                        children[i].RetrieveLeaves(frustumPlanes, visibleBoundsList, visibleIDList);
                    }
                }
            }
        }

        public bool FindLeaf(Vector3 point, int index)
        {
            if (bounds.Contains(point))
            {
                if (children.Count != 0)
                {
                    for (int i = 0; i < children.Count; i++)
                    {
                        if (children[i].FindLeaf(point, index))
                        {
                            return true;
                        }
                    }
                }
                else
                {
                    grassIndices.Add(index);
                    return true;
                }
            }
            return false;
        }

        public void ReturnLeafList(Vector3 hitPoint, List<int> resultList, float radius)
        {
            Bounds queryBounds = new Bounds(hitPoint, Vector3.one * (radius * 2f));
            if (bounds.Intersects(queryBounds))
            {
                if (children.Count == 0)
                {
                    if (grassIndices.Count > 0)
                    {
                        resultList.AddRange(grassIndices);
                    }
                }
                else
                {
                    for (int i = 0; i < children.Count; i++)
                    {
                        children[i].ReturnLeafList(hitPoint, resultList, radius);
                    }
                }
            }
        }

        public void ClearEmpty()
        {
            for (int i = children.Count - 1; i >= 0; i--)
            {
                children[i].ClearEmpty();
                if (children[i].children.Count == 0 && children[i].grassIndices.Count == 0)
                {
                    children.RemoveAt(i);
                }
            }
        }

        public void RetrieveAllLeaves(List<ModernCullingTreeNode> leaves)
        {
            if (children.Count == 0)
            {
                leaves.Add(this);
            }
            else
            {
                for (int i = 0; i < children.Count; i++)
                {
                    children[i].RetrieveAllLeaves(leaves);
                }
            }
        }
    }
}
