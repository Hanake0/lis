import React from 'react';
const DrawerCtx = React.createContext<unknown>(null);
export const DrawerProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => (
	<DrawerCtx.Provider value={null}>{children}</DrawerCtx.Provider>
);
const EMPTY: Record<string, never> = {};
export function useDrawer(): unknown { return EMPTY; }
