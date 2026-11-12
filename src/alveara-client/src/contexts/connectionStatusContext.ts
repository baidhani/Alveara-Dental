import { createContext, useContext } from "react";
import type { ConnectionStatus } from "../hooks/useConnectionStatus";

/** ALV-N020 (F-012): the one health-check result, published by the root layout so a page (the sign-in page) can read it without starting a second poll. */
export const ConnectionStatusContext = createContext<ConnectionStatus>("checking");

export function useServerConnection(): ConnectionStatus {
  return useContext(ConnectionStatusContext);
}
