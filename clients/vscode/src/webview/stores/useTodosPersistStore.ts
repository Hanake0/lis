import { create } from 'zustand';
const noop: any = () => {};
export const useTodosPersistStore = create<any>(() => ({
	todos: [],
	sessions: {} as Record<string, { todos?: unknown[] }>,
	setTodos: noop,
}));
