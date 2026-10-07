import type { AnchorHTMLAttributes, ReactNode } from "react";

/** next/link for the demo: an ordinary anchor into the hash route. */
export default function Link({ href, children, ...rest }: { href: string; children?: ReactNode; prefetch?: boolean } & Omit<AnchorHTMLAttributes<HTMLAnchorElement>, "href">) {
  const { prefetch: _p, ...a } = rest as { prefetch?: boolean } & AnchorHTMLAttributes<HTMLAnchorElement>;
  void _p;
  return <a href={href.startsWith("/") && !href.startsWith("/api/") ? "#" + href : href} {...a}>{children}</a>;
}
