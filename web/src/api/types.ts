export type HoldStatus = 'Active' | 'Released' | 'Expired';

export interface HoldItem {
  sku: string;
  quantity: number;
}

export interface Hold {
  id: string;
  status: HoldStatus;
  items: HoldItem[];
  createdAt: string;
  expiresAt: string;
  releasedAt: string | null;
}

export interface CreateHoldRequest {
  items: HoldItem[];
  ttlMinutes?: number;
}

export interface InventoryItem {
  sku: string;
  available: number;
  held: number;
  total: number;
}

export interface InventoryPage {
  items: InventoryItem[];
  skip: number;
  take: number;
  totalCount: number;
}
