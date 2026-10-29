import React from 'react';

// Document-page thumbnail for the file-attachment pattern on the submission detail page — exported
// from the Figma design (node 420-80813): a white A4 page with placeholder content and a soft shadow.
const AttachmentThumbnailIcon = (): React.ReactElement => (
  <svg width="103" height="144" viewBox="0 0 103 144" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
    <g filter="url(#attachment-thumbnail-shadow)">
      <rect width="99" height="140" transform="translate(2)" fill="white" />
      <path d="M14 12H89V39H14V12ZM14 59H32.75V122H14V59ZM69 61V120H53V61H69ZM71 59H51V122H71V59Z" fill="#b1b4b6" />
      <path d="M51 61.05V120H34.8V61.05H51ZM53 59.05H32.75V122.05H53V59V59.05ZM87 61.05V120H71.05V61.05H87ZM89 59.05H69V122.05H89V59V59.05Z" fill="#b1b4b6" />
    </g>
    <defs>
      <filter id="attachment-thumbnail-shadow" x="0" y="0" width="103" height="144" filterUnits="userSpaceOnUse" colorInterpolationFilters="sRGB">
        <feFlood floodOpacity="0" result="BackgroundImageFix" />
        <feColorMatrix in="SourceAlpha" type="matrix" values="0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 127 0" result="hardAlpha" />
        <feOffset dy="2" />
        <feGaussianBlur stdDeviation="1" />
        <feColorMatrix type="matrix" values="0 0 0 0 0.694118 0 0 0 0 0.705882 0 0 0 0 0.713726 0 0 0 1 0" />
        <feBlend mode="normal" in2="BackgroundImageFix" result="effect1_dropShadow" />
        <feBlend mode="normal" in="SourceGraphic" in2="effect1_dropShadow" result="shape" />
      </filter>
    </defs>
  </svg>
);

export default AttachmentThumbnailIcon;
