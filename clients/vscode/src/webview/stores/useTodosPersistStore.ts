import { create } from 'zustand';
export const useTodosPersistStore = create(() => ({ todos: [] }));
