import React, { useState, useRef, useEffect, useCallback } from 'react';
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

const GovUKActionMenu = ({ items, buttonLabel = 'Actions', testId }: GovUKActionMenuProps): React.ReactElement => {
  const [isOpen, setIsOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(-1);
  const [menuAlignment, setMenuAlignment] = useState<'left' | 'right'>('right');
  const [menuMaxWidth, setMenuMaxWidth] = useState<number>();
  const containerRef = useRef<HTMLDivElement>(null);
  const toggleRef = useRef<HTMLButtonElement>(null);
  const itemRefs = useRef<Array<HTMLButtonElement | null>>([]);
  const navigate = useNavigate();

  const closeMenu = useCallback((returnFocus: boolean) => {
    setIsOpen(false);
    setActiveIndex(-1);
    if (returnFocus) {
      toggleRef.current?.focus();
    }
  }, []);

  const updateMenuPosition = useCallback(() => {
    const container = containerRef.current;
    if (!container || typeof window === 'undefined') {
      return;
    }

    const rect = container.getBoundingClientRect();
    const viewportWidth = document.documentElement.clientWidth || window.innerWidth;
    const viewportGutter = 15;
    const roomToRight = Math.max(0, viewportWidth - rect.left - viewportGutter);
    const roomToLeft = Math.max(0, rect.right - viewportGutter);
    const alignment = roomToRight >= roomToLeft ? 'left' : 'right';

    setMenuAlignment(alignment);
    setMenuMaxWidth(Math.floor(alignment === 'left' ? roomToRight : roomToLeft));
  }, []);

  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        closeMenu(false);
      }
    };

    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, [closeMenu]);

  useEffect(() => {
    if (!isOpen) {
      return;
    }

    updateMenuPosition();
    window.addEventListener('resize', updateMenuPosition);
    return () => window.removeEventListener('resize', updateMenuPosition);
  }, [isOpen, updateMenuPosition]);

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
        <ul
          className={`govuk-action-menu__list govuk-action-menu__list--align-${menuAlignment}`}
          role="menu"
          aria-label={buttonLabel}
          style={{ maxWidth: menuMaxWidth ? `${menuMaxWidth}px` : undefined }}
          data-testid={composeTestId(testId, 'list')}>
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
