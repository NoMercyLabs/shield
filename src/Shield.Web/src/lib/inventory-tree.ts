import type { Ecosystem, InventoryItemResponse } from '@/types/api'

// Synthetic nodes fill in chain segments that have no inventory row of their own.
// They carry an id of the form `synthetic:<ecosystem>:<chain>` so they're stable
// across renders but distinct from real item ids.
export interface TreeNode {
  id: string
  item: InventoryItemResponse | null
  name: string
  version: string | null
  isDirect: boolean
  isSynthetic: boolean
  depth: number
  children: TreeNode[]
}

export interface EcosystemGroup {
  ecosystem: Ecosystem
  directRoots: TreeNode[]
  orphanRoots: TreeNode[]
  totalCount: number
}

function parseChain(raw: string): string[] {
  if (!raw) return []
  try {
    const parsed = JSON.parse(raw) as unknown
    if (!Array.isArray(parsed)) return []
    return parsed.filter((segment): segment is string => typeof segment === 'string')
  }
  catch {
    return []
  }
}

function isDirectDep(item: InventoryItemResponse): boolean {
  return item.isDirect || parseChain(item.parentChain).length === 0
}

function makeRealNode(item: InventoryItemResponse, depth: number): TreeNode {
  return {
    id: `item:${item.id}`,
    item,
    name: item.name,
    version: item.version,
    isDirect: item.isDirect,
    isSynthetic: false,
    depth,
    children: [],
  }
}

function makeSyntheticNode(ecosystem: Ecosystem, chainKey: string, name: string, depth: number): TreeNode {
  return {
    id: `synthetic:${ecosystem}:${chainKey}`,
    item: null,
    name,
    version: null,
    isDirect: false,
    isSynthetic: true,
    depth,
    children: [],
  }
}

function sortNodes(nodes: TreeNode[]): void {
  nodes.sort((nodeA, nodeB) => {
    if (nodeA.isDirect !== nodeB.isDirect) return nodeA.isDirect ? -1 : 1
    return nodeA.name.localeCompare(nodeB.name)
  })
  for (const node of nodes) sortNodes(node.children)
}

export function buildInventoryTree(items: InventoryItemResponse[]): EcosystemGroup[] {
  const byEcosystem = new Map<Ecosystem, InventoryItemResponse[]>()
  for (const item of items) {
    const bucket = byEcosystem.get(item.ecosystem)
    if (bucket) bucket.push(item)
    else byEcosystem.set(item.ecosystem, [item])
  }

  const groups: EcosystemGroup[] = []
  for (const [ecosystem, bucket] of byEcosystem) {
    // Direct deps keyed by name. If two direct items share a name we keep both
    // as siblings under the ecosystem root and attach transitives to the first
    // (rare; ParentChain[0] is just a name string with no version qualifier).
    const directByName = new Map<string, TreeNode>()
    const directRoots: TreeNode[] = []
    const orphanRoots: TreeNode[] = []

    const transitives: InventoryItemResponse[] = []
    for (const item of bucket) {
      if (isDirectDep(item)) {
        const node = makeRealNode(item, 0)
        directRoots.push(node)
        if (!directByName.has(item.name)) directByName.set(item.name, node)
      }
      else {
        transitives.push(item)
      }
    }

    for (const item of transitives) {
      const chain = parseChain(item.parentChain)
      if (chain.length === 0) {
        // Marked transitive but no chain — treat as orphan rather than guess.
        orphanRoots.push(makeRealNode(item, 0))
        continue
      }

      const root = directByName.get(chain[0])
      if (!root) {
        orphanRoots.push(makeRealNode(item, 0))
        continue
      }

      // Walk/create intermediate synthetic nodes for chain[1..end], then attach
      // the real item as a leaf under the deepest segment.
      let cursor = root
      for (let depth = 1; depth < chain.length; depth++) {
        const segment = chain[depth]
        const chainKey = chain.slice(0, depth + 1).join('>')
        let next = cursor.children.find(child => child.name === segment && !child.isSynthetic)
          ?? cursor.children.find(child => child.name === segment)
        if (!next) {
          next = makeSyntheticNode(item.ecosystem, chainKey, segment, depth)
          cursor.children.push(next)
        }
        cursor = next
      }

      cursor.children.push(makeRealNode(item, chain.length))
    }

    sortNodes(directRoots)
    sortNodes(orphanRoots)

    groups.push({
      ecosystem,
      directRoots,
      orphanRoots,
      totalCount: bucket.length,
    })
  }

  groups.sort((groupA, groupB) => groupA.ecosystem - groupB.ecosystem)
  return groups
}
