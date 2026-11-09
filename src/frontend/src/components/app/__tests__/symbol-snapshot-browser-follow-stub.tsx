// The real button needs the app's server runtime, which the browser fixture does not have.
export function FollowSymbolButton({ symbol }: { symbol?: string }) {
  return <button type="button">دنبال کردن {symbol}</button>;
}
