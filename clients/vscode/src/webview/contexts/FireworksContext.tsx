import React from 'react';
const FireworksContext = React.createContext<unknown>(null);
export const FireworksProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => (
	<FireworksContext.Provider value={null}>{children}</FireworksContext.Provider>
);
export function useFireworks() { return { fire: () => {} }; }
export function useFireworksCelebration(..._args: any[]): any { return null; }
