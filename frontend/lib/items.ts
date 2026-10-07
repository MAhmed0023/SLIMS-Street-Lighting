import type { ItemDto } from "./types";

/** One line per item code: the units behind it are still tracked one by one, but people count in quantities. */
export interface ItemLine { itemCode: string; description: string; uom: string; qty: number; value: number; status: string; location: string }

export function byItemCode(items: ItemDto[]): ItemLine[] {
  const m = new Map<string, ItemDto[]>();
  for (const i of items) m.set(i.itemCode, [...(m.get(i.itemCode) ?? []), i]);
  return [...m.values()].map((g) => {
    const st = new Map<string, number>();
    for (const i of g) st.set(i.status, (st.get(i.status) ?? 0) + i.qty);
    return {
      itemCode: g[0].itemCode, description: g[0].description, uom: g[0].uom,
      qty: g.reduce((s, i) => s + i.qty, 0), value: g.reduce((s, i) => s + i.qty * i.rate, 0),
      status: st.size === 1 ? g[0].status : [...st].map(([k, v]) => `${v} ${k}`).join(", "),
      location: [...new Set(g.map((i) => i.locationCode).filter(Boolean))].join(", "),
    };
  });
}
