import { createContext, useCallback, useContext, useState } from "react";
import type { ReactNode } from "react";
import "./Notification.css";

export type NotificationTone = "info" | "success" | "warning" | "danger";

interface NotificationItem {
  id: number;
  tone: NotificationTone;
  message: string;
}

interface NotificationContextValue {
  notify: (tone: NotificationTone, message: string) => void;
}

const NotificationContext = createContext<NotificationContextValue | null>(null);

let nextId = 1;

export function NotificationProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<NotificationItem[]>([]);

  const notify = useCallback((tone: NotificationTone, message: string) => {
    const id = nextId++;
    setItems((prev) => [...prev, { id, tone, message }]);
    setTimeout(() => setItems((prev) => prev.filter((n) => n.id !== id)), 5000);
  }, []);

  return (
    <NotificationContext.Provider value={{ notify }}>
      {children}
      <div className="alv-notification-region" role="status" aria-live="polite">
        {items.map((item) => (
          <div key={item.id} className={`alv-notification alv-notification--${item.tone}`}>
            {item.message}
          </div>
        ))}
      </div>
    </NotificationContext.Provider>
  );
}

export function useNotifications() {
  const ctx = useContext(NotificationContext);
  if (!ctx) throw new Error("useNotifications must be used within a NotificationProvider");
  return ctx;
}
