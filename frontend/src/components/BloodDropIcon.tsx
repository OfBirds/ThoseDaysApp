interface BloodDropIconProps {
  color?: string;
  size?: number;
}

function BloodDropIcon({ color = 'currentColor', size = 20 }: BloodDropIconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill={color}
      xmlns="http://www.w3.org/2000/svg"
      role="img"
      aria-label="Blood drop icon"
    >
      <path d="M12 2c0 0-6 9-6 13c0 3.313 2.686 6 6 6s6-2.687 6-6c0-4-6-13-6-13z" />
    </svg>
  );
}

export default BloodDropIcon;
