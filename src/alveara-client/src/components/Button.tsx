import { forwardRef } from "react";
import type { ButtonHTMLAttributes } from "react";
import { Link } from "react-router-dom";
import type { LinkProps } from "react-router-dom";
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

interface ButtonLinkProps extends LinkProps {
  variant?: ButtonVariant;
}

/**
 * A navigation action styled like Button, rendered as a single <a> element
 * (react-router's Link) rather than nesting a <button> inside a link —
 * nested interactive elements are invalid HTML and confuse keyboard/screen
 * reader navigation (found in ALV-N001 R01 review, N001-R01 notes).
 */
export function ButtonLink({ variant = "secondary", className, ...rest }: ButtonLinkProps) {
  const classes = ["alv-button", `alv-button--${variant}`, className].filter(Boolean).join(" ");
  return <Link className={classes} {...rest} />;
}
