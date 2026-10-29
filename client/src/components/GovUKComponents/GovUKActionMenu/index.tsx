import React, { useState, useRef, useEffect, useCallback, useLayoutEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { composeTestId, slugifyTestId } from '../../../helpers/testIds';
import './styles.css';

export interface ActionMenuItem {
  label: string;
  href?: string;
  onClick?: () => void | Promise<void>;
  dividerBefore?: boolean;
  testId?: string;
}

export interface GovUKActionMenuProps {
  items: ActionMenuItem[];
  buttonLabel?: string;
  testId?: string;
}

// Every ancestor whose overflow clips the button, short of the page itself — not just the nearest, so a clipping cell
// or panel between the button and a scrolling table box cannot hide the box from the check.
const clippingAncestors = (element: Element | null): Element[] => {
  const found: Element[] = [];
  for (let node = element?.parentElement; node && node !== document.body; node = node.parentElement) {
    const { overflowX, overflowY } = getComputedStyle(node);
    if (overflowX !== 'visible' || overflowY !== 'visible') {
      found.push(node);
    }
  }
  return found;
};

const GovUKActionMenu = ({ items, buttonLabel = 'Actions', testId }: GovUKActionMenuProps): React.ReactElement => {
  const [isOpen, setIsOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(-1);
  const [menuPosition, setMenuPosition] = useState<React.CSSProperties>();
  const containerRef = useRef<HTMLDivElement>(null);
  const toggleRef = useRef<HTMLButtonElement>(null);
  const listRef = useRef<HTMLUListElement>(null);
  const itemRefs = useRef<Array<HTMLButtonElement | null>>([]);
  const navigate = useNavigate();

  const closeMenu = useCallback((returnFocus: boolean, preventScroll = false) => {
    setIsOpen(false);
    setActiveIndex(-1);
    if (returnFocus) {
      toggleRef.current?.focus({ preventScroll });
    }
  }, []);

  // The menu is position: fixed, placed against the button from the button's own position, rather than absolute inside
  // it: an absolute menu is clipped by any scrolling ancestor, and a table that is too wide for its column scrolls in
  // a GovUKTableScroll box, where a row's Actions menu would be cut off at the box's edge (BLUM-9350). The menu stays
  // where it is in the DOM, so the tab order, reading order and click-outside test are unchanged.
  // A fixed menu is outside the page's scrollable area, so focusing an item can never scroll the page to it: the menu
  // has to fit the viewport itself. It opens below the button when it fits there, otherwise on whichever side has more
  // room, capped to that room and scrolling inside itself — at 400% zoom neither side may hold all of it. Across, it
  // opens towards the side with more room, at least as wide as the button and never past the viewport's 15px gutter.
  const lastPosition = useRef('');
  const updateMenuPosition = useCallback(() => {
    const container = containerRef.current;
    if (!container || typeof window === 'undefined') {
      return;
    }

    const rect = container.getBoundingClientRect();
    const viewportWidth = document.documentElement.clientWidth || window.innerWidth;
    const viewportHeight = document.documentElement.clientHeight || window.innerHeight;
    const viewportGutter = 15;
    const roomToRight = Math.max(0, viewportWidth - rect.left - viewportGutter);
    const roomToLeft = Math.max(0, rect.right - viewportGutter);
    const alignLeft = roomToRight >= roomToLeft;
    const maxWidth = Math.floor(alignLeft ? roomToRight : roomToLeft);

    // scrollHeight is the menu's full height whatever its current cap. It is 0 before the first render, which places the
    // menu below until the layout effect measures it, before anything is painted.
    const roomBelow = Math.max(0, viewportHeight - rect.bottom - viewportGutter);
    const roomAbove = Math.max(0, rect.top - viewportGutter);
    const menuHeight = listRef.current?.scrollHeight ?? 0;
    const openBelow = menuHeight <= roomBelow || roomBelow >= roomAbove;
    // Never under one item's height, so a button jammed against the viewport's edge still shows a usable menu.
    const maxHeight = Math.max(Math.floor(openBelow ? roomBelow : roomAbove), 40);

    // The button's width goes in as a custom property rather than an inline min-width, so a page's own min-width on
    // the menu (Admin/Users/View) still wins.
    const position = {
      top: openBelow ? rect.bottom : undefined,
      bottom: openBelow ? undefined : viewportHeight - rect.top,
      left: alignLeft ? rect.left : undefined,
      right: alignLeft ? undefined : viewportWidth - rect.right,
      maxWidth: maxWidth ? `${maxWidth}px` : undefined,
      maxHeight: `${maxHeight}px`,
      '--app-action-menu-button-width': `${rect.width}px`,
    } as React.CSSProperties;
    // Only a real move re-renders: this runs on every frame while the menu is open.
    const key = JSON.stringify(position);
    if (key !== lastPosition.current) {
      lastPosition.current = key;
      setMenuPosition(position);
    }
  }, []);

  // A menu whose button the user scrolls out of any box that clips it — a GovUKTableScroll box, or any other overflow
  // container, checked all the way up — closes rather than float detached from it, handing the focus back to the button if it was in the menu,
  // without scrolling the button back into view and undoing the user's scroll. Checked while following only: opening
  // first brings a half-hidden button fully into view (below), so a tap on one opens its menu instead of closing it.
  const clippingAncestorsRef = useRef<Element[]>([]);
  const followButton = useCallback(() => {
    const container = containerRef.current;
    if (container && listRef.current && clippingAncestorsRef.current.length > 0) {
      const rect = container.getBoundingClientRect();
      const x = rect.left + rect.width / 2;
      const y = rect.top + rect.height / 2;
      const clipped = clippingAncestorsRef.current.some((ancestor) => {
        const clip = ancestor.getBoundingClientRect();
        return x < clip.left || x > clip.right || y < clip.top || y > clip.bottom;
      });
      if (clipped) {
        closeMenu(listRef.current.contains(document.activeElement), true);
        return;
      }
    }
    updateMenuPosition();
  }, [closeMenu, updateMenuPosition]);

  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        closeMenu(false);
      }
    };

    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, [closeMenu]);

  // A fixed menu has to follow its button, and the button moves for many reasons: the page or a table scrolling, the
  // window resizing, or content above it being added or removed — a refetched row, a banner — which fires no event at
  // all (and does not resize <body>, which is 0px high in this app). So while the menu is open it re-reads the button's
  // position once a frame, as floating-ui's autoUpdate({ animationFrame: true }) does: one layout read a frame (a few
  // rects, no writes in between), and a re-render only when the position actually changed. A layout effect, so the
  // menu is measured and placed on the side it fits before it is first painted, and before the focus effect below
  // moves into it.
  useLayoutEffect(() => {
    if (!isOpen) {
      return;
    }

    // A button partly scrolled out of its table's box comes fully into view first, so the menu opens against all of it.
    toggleRef.current?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    clippingAncestorsRef.current = clippingAncestors(containerRef.current);
    updateMenuPosition();

    let frame = requestAnimationFrame(function follow() {
      followButton();
      frame = requestAnimationFrame(follow);
    });
    return () => cancelAnimationFrame(frame);
  }, [isOpen, updateMenuPosition, followButton]);

  useEffect(() => {
    if (isOpen && activeIndex >= 0) {
      itemRefs.current[activeIndex]?.focus();
    }
  }, [isOpen, activeIndex]);

  const handleItemClick = async (item: ActionMenuItem) => {
    closeMenu(true);
    if (item.onClick) {
      await item.onClick();
    } else if (item.href) {
      navigate(item.href);
    }
  };

  const handleToggleKeyDown = (event: React.KeyboardEvent<HTMLButtonElement>) => {
    if (event.key === 'ArrowDown' || event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      updateMenuPosition();
      setIsOpen(true);
      setActiveIndex(0);
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      updateMenuPosition();
      setIsOpen(true);
      setActiveIndex(items.length - 1);
    } else if (event.key === 'Escape' && isOpen) {
      event.preventDefault();
      closeMenu(false);
    }
  };

  const handleItemKeyDown = (event: React.KeyboardEvent<HTMLButtonElement>, index: number) => {
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        setActiveIndex((index + 1) % items.length);
        break;
      case 'ArrowUp':
        event.preventDefault();
        setActiveIndex((index - 1 + items.length) % items.length);
        break;
      case 'Home':
        event.preventDefault();
        setActiveIndex(0);
        break;
      case 'End':
        event.preventDefault();
        setActiveIndex(items.length - 1);
        break;
      case 'Escape':
        event.preventDefault();
        closeMenu(true);
        break;
      case 'Tab':
        closeMenu(false);
        break;
      default:
        break;
    }
  };

  return (
    <div className="govuk-action-menu" ref={containerRef} data-testid={testId}>
      <button
        ref={toggleRef}
        type="button"
        className="govuk-button govuk-button--secondary govuk-action-menu__button"
        onClick={() => {
          if (!isOpen) {
            updateMenuPosition();
          }
          setIsOpen((open) => !open);
          setActiveIndex(-1);
        }}
        onKeyDown={handleToggleKeyDown}
        aria-expanded={isOpen}
        aria-haspopup="menu"
        data-testid={composeTestId(testId, 'toggle')}>
        {buttonLabel}
        <span className={`govuk-action-menu__chevron ${isOpen ? 'govuk-action-menu__chevron--open' : ''}`} aria-hidden="true">
          <svg width="12" height="7" viewBox="0 0 12 7" fill="currentColor" xmlns="http://www.w3.org/2000/svg">
            <path d="M6 7L12 0H0L6 7Z" />
          </svg>
        </span>
      </button>
      {isOpen && (
        <ul ref={listRef} className="govuk-action-menu__list" role="menu" aria-label={buttonLabel} style={menuPosition} data-testid={composeTestId(testId, 'list')}>
          {items.map((item, index) => (
            <React.Fragment key={index}>
              {item.dividerBefore && <li className="govuk-action-menu__divider" role="separator" />}
              <li role="none">
                <button
                  ref={(el) => {
                    itemRefs.current[index] = el;
                    itemRefs.current.length = items.length;
                  }}
                  type="button"
                  role="menuitem"
                  tabIndex={activeIndex === index ? 0 : -1}
                  className="govuk-action-menu__item"
                  onClick={() => handleItemClick(item)}
                  onKeyDown={(event) => handleItemKeyDown(event, index)}
                  data-testid={item.testId ?? composeTestId(testId, 'item', slugifyTestId(item.label))}>
                  {item.label}
                </button>
              </li>
            </React.Fragment>
          ))}
        </ul>
      )}
    </div>
  );
};

export default GovUKActionMenu;
