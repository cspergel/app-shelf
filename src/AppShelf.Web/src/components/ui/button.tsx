import * as React from "react";
import { cva, type VariantProps } from "class-variance-authority";
import { cn } from "@/lib/utils";

const buttonVariants = cva(
  "inline-flex items-center justify-center gap-1.5 whitespace-nowrap rounded-md text-[13px] font-medium transition-all duration-150 focus-visible:outline-none disabled:pointer-events-none disabled:opacity-50 select-none",
  {
    variants: {
      variant: {
        primary:
          "bg-accent text-white shadow-[0_1px_2px_rgba(0,0,0,0.4)] hover:bg-accent-hover active:translate-y-px",
        ghost:
          "bg-transparent text-text-secondary hover:bg-surface-elevated hover:text-text-primary",
        outline:
          "border border-hairline bg-transparent text-text-secondary hover:bg-surface-elevated hover:text-text-primary",
      },
      size: {
        default: "h-8 px-3.5",
        sm: "h-7 px-2.5 text-xs",
        icon: "h-8 w-8 p-0",
      },
    },
    defaultVariants: { variant: "primary", size: "default" },
  },
);

export interface ButtonProps
  extends React.ButtonHTMLAttributes<HTMLButtonElement>,
    VariantProps<typeof buttonVariants> {}

const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant, size, ...props }, ref) => (
    <button
      ref={ref}
      className={cn(buttonVariants({ variant, size }), className)}
      {...props}
    />
  ),
);
Button.displayName = "Button";

export { Button, buttonVariants };
