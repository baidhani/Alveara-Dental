import { forwardRef } from "react";
import type { ButtonHTMLAttributes } from "react";
import "./Button.css";

export type ButtonVariant = "primary" | "secondary" | "danger";

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  ({ variant = "secondary", className, ...rest }, ref) => {
    const classes = ["alv-button", `alv-button--${variant}`, className].filter(Boolean).join(" ");
    return <button ref={ref} className={classes} {...rest} />;
  }
);

Button.displayName = "Button";
