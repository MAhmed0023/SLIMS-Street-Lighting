"use client";
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { api, get, post, setToken, getToken } from "./api";
import type { AppConfig, MetaDto, TokenResponse, UserDto } from "./types";

interface AuthState {
  user: UserDto | null; config: AppConfig | null; meta: MetaDto | null; ready: boolean;
  login: (id: string, password: string) => Promise<TokenResponse>;
  logout: () => Promise<void>;
  reload: () => Promise<void>;
  can: (perm: string) => boolean;
}
const Ctx = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserDto | null>(null);
  const [config, setConfig] = useState<AppConfig | null>(null);
  const [meta, setMeta] = useState<MetaDto | null>(null);
  const [ready, setReady] = useState(false);

  const loadSession = useCallback(async () => {
    try {
      const m = await get<MetaDto>("/api/meta"); setMeta(m);
      // Use the token this tab already holds; otherwise ask the refresh cookie once (no token = no /me call).
      let me: UserDto | null = null;
      if (getToken()) { try { me = await get<UserDto>("/api/auth/me"); } catch { me = null; } }
      if (!me) { try { const t = await post<TokenResponse>("/api/auth/refresh"); setToken(t.accessToken); me = t.user; } catch { setToken(null); me = null; } }
      setUser(me);
      setConfig(me ? await get<AppConfig>("/api/config") : null);
    } finally { setReady(true); }
  }, []);
  useEffect(() => { loadSession(); }, [loadSession]);

  const login = useCallback(async (id: string, password: string) => {
    const t = await post<TokenResponse>("/api/auth/login", { id, password });
    setToken(t.accessToken); setUser(t.user);
    setConfig(await get<AppConfig>("/api/config"));
    setMeta(await get<MetaDto>("/api/meta"));
    return t;
  }, []);
  const logout = useCallback(async () => {
    try { await api("POST", "/api/auth/logout"); } catch { /* already out */ }
    setToken(null); setUser(null); setConfig(null);
  }, []);
  const can = useCallback((perm: string) => !!user?.permissions?.[perm], [user]);
  const value = useMemo(() => ({ user, config, meta, ready, login, logout, reload: loadSession, can }), [user, config, meta, ready, login, logout, loadSession, can]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useAuth() {
  const v = useContext(Ctx); if (!v) throw new Error("useAuth outside AuthProvider");
  return v;
}
