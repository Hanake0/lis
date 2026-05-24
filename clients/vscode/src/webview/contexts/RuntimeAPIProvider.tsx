import React from 'react';
import type { RuntimeAPIs } from '@/lib/api/types';

const RuntimeAPIContext = React.createContext<RuntimeAPIs>({});

export const RuntimeAPIProvider: React.FC<{ apis: RuntimeAPIs; children: React.ReactNode }> = ({ apis, children }) => (
	<RuntimeAPIContext.Provider value={apis}>{children}</RuntimeAPIContext.Provider>
);
