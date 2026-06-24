"""Channel list widget for TeleClean.

Displays channels with checkboxes, search, filters, sorting, pagination,
and a selection counter.
"""

from __future__ import annotations

import logging
from typing import Callable, Optional

from PySide6.QtCore import Qt, Signal, Slot
from PySide6.QtWidgets import (
    QCheckBox,
    QComboBox,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QListWidget,
    QListWidgetItem,
    QPushButton,
    QVBoxLayout,
    QWidget,
)

from app.models.channel import Channel

logger = logging.getLogger(__name__)

PAGE_SIZE = 50


class ChannelItemWidget(QWidget):
    """Custom widget for a single channel row inside the QListWidget."""

    toggled = Signal(int, bool)  # channel_id, checked

    def __init__(self, channel: Channel, parent: Optional[QWidget] = None) -> None:
        super().__init__(parent)
        self.channel = channel

        layout = QHBoxLayout(self)
        layout.setContentsMargins(8, 4, 8, 4)
        layout.setSpacing(12)

        # Checkbox
        self.checkbox = QCheckBox()
        self.checkbox.stateChanged.connect(self._on_toggle)
        layout.addWidget(self.checkbox)

        # Channel info
        info_layout = QVBoxLayout()
        info_layout.setSpacing(2)

        title_label = QLabel(channel.title)
        title_label.setStyleSheet("font-weight: 600; font-size: 14px;")
        info_layout.addWidget(title_label)

        # Subtitle: subscribers + unread
        parts = []
        if channel.participant_count > 0:
            parts.append(f"👤 {channel.participant_count:,}")
        if channel.unread_count > 0:
            parts.append(f"💬 {channel.unread_count} непрочитанных")
        if channel.username:
            parts.append(f"@{channel.username}")

        subtitle_label = QLabel(" | ".join(parts) if parts else "Нет данных")
        subtitle_label.setStyleSheet("font-size: 12px; color: #8e9ba4;")
        info_layout.addWidget(subtitle_label)

        layout.addLayout(info_layout, 1)

    def _on_toggle(self, state: int) -> None:
        self.toggled.emit(self.channel.id, state == Qt.Checked.value)

    def set_checked(self, checked: bool) -> None:
        """Programmatically set the checkbox state without emitting signal."""
        self.checkbox.blockSignals(True)
        self.checkbox.setChecked(checked)
        self.checkbox.blockSignals(False)


class ChannelListWidget(QWidget):
    """Main channel list widget with search, filter, sort, and pagination.

    Signals
    -------
    selection_changed(count: int)
        Emitted when the selection count changes.
    leave_requested(channel_ids: list[int])
        Emitted when the user requests to leave selected channels.
    """

    selection_changed = Signal(int)
    leave_requested = Signal(list)

    def __init__(self, parent: Optional[QWidget] = None) -> None:
        super().__init__(parent)

        # Data
        self._all_channels: list[Channel] = []
        self._filtered_channels: list[Channel] = []
        self._page: int = 0
        self._selected_ids: set[int] = set()
        self._item_widgets: dict[int, ChannelItemWidget] = {}

        # Callbacks for custom filter/sort (set externally by main_window)
        self._filter_callbacks: list[Callable[[list[Channel]], list[Channel]]] = []
        self._sort_callback: Optional[Callable[[list[Channel]], list[Channel]]] = None
        self._on_load_more: Optional[Callable[[], None]] = None

        self._build_ui()

    # ------------------------------------------------------------------
    # UI construction
    # ------------------------------------------------------------------

    def _build_ui(self) -> None:
        """Create and arrange all child widgets."""
        main_layout = QVBoxLayout(self)
        main_layout.setContentsMargins(0, 0, 0, 0)
        main_layout.setSpacing(8)

        # --- Toolbar ---
        toolbar = QHBoxLayout()
        toolbar.setSpacing(8)

        # Search
        self._search_input = QLineEdit()
        self._search_input.setPlaceholderText("🔍  Поиск каналов...")
        self._search_input.setStyleSheet("min-width: 200px;")
        self._search_input.textChanged.connect(self._on_search)
        toolbar.addWidget(self._search_input)

        # Filter dropdown
        self._filter_combo = QComboBox()
        self._filter_combo.addItems(["Все каналы", "Непрочитанные", "Мало подписчиков"])
        self._filter_combo.currentIndexChanged.connect(self._on_filter_changed)
        toolbar.addWidget(self._filter_combo)

        # Sort dropdown
        self._sort_combo = QComboBox()
        self._sort_combo.addItems(["По названию", "По дате", "По подписчикам"])
        self._sort_combo.currentIndexChanged.connect(self._on_sort_changed)
        toolbar.addWidget(self._sort_combo)

        main_layout.addLayout(toolbar)

        # --- Selection info bar ---
        info_bar = QHBoxLayout()
        self._selection_label = QLabel("Выбрано: 0")
        self._selection_label.setStyleSheet(
            "font-size: 13px; color: #8e9ba4; padding: 4px 0px;"
        )
        info_bar.addWidget(self._selection_label)

        info_bar.addStretch()

        self._select_all_btn = QPushButton("Выбрать всё на странице")
        self._select_all_btn.setObjectName("linkButton")
        self._select_all_btn.clicked.connect(self._on_select_all_page)
        info_bar.addWidget(self._select_all_btn)

        main_layout.addLayout(info_bar)

        # --- Channel list ---
        self._list_widget = QListWidget()
        self._list_widget.setVerticalScrollMode(QListWidget.ScrollPerPixel)
        self._list_widget.verticalScrollBar().valueChanged.connect(
            self._on_scroll
        )
        main_layout.addWidget(self._list_widget, 1)

    # ------------------------------------------------------------------
    # Data loading
    # ------------------------------------------------------------------

    def set_channels(self, channels: list[Channel]) -> None:
        """Set the complete channel list (replaces existing data)."""
        self._all_channels = channels
        self._page = 0
        self._selected_ids.clear()
        self._filtered_channels = self._apply_filters()
        self._render_page()
        self.selection_changed.emit(0)

    def set_load_more_callback(self, callback: Callable[[], None]) -> None:
        """Set a callback invoked when the user scrolls to the bottom."""
        self._on_load_more = callback

    # ------------------------------------------------------------------
    # Filter / Sort
    # ------------------------------------------------------------------

    def set_filter_callback(
        self, callback: Callable[[list[Channel]], list[Channel]]
    ) -> None:
        """Add a custom filter function."""
        self._filter_callbacks.append(callback)

    def set_sort_callback(
        self, callback: Callable[[list[Channel]], list[Channel]]
    ) -> None:
        """Set a custom sort function."""
        self._sort_callback = callback

    def _apply_filters(self) -> list[Channel]:
        """Apply all active filters and sorting to the full channel list."""
        result = self._all_channels[:]

        # Built-in search
        query = self._search_input.text().strip().lower()
        if query:
            result = [ch for ch in result if query in ch.title.lower()]

        # Built-in filter (unread / low subscribers)
        filter_idx = self._filter_combo.currentIndex()
        if filter_idx == 1:  # unread
            result = [ch for ch in result if ch.unread_count > 0]
        elif filter_idx == 2:  # low subscribers
            result = [ch for ch in result if ch.participant_count <= 100]

        # External filter callbacks
        for cb in self._filter_callbacks:
            result = cb(result)

        # Sorting
        sort_idx = self._sort_combo.currentIndex()
        if sort_idx == 0:  # by title
            result.sort(key=lambda c: c.title.lower())
        elif sort_idx == 1:  # by date (unread count as proxy)
            result.sort(key=lambda c: c.unread_count, reverse=True)
        elif sort_idx == 2:  # by subscribers
            result.sort(key=lambda c: c.participant_count, reverse=True)

        if self._sort_callback:
            result = self._sort_callback(result)

        return result

    def _render_page(self) -> None:
        """Render the current page of filtered channels."""
        self._list_widget.clear()
        self._item_widgets.clear()

        start = self._page * PAGE_SIZE
        end = start + PAGE_SIZE
        page_channels = self._filtered_channels[start:end]

        for channel in page_channels:
            item = QListWidgetItem(self._list_widget)
            widget = ChannelItemWidget(channel)
            widget.toggled.connect(self._on_item_toggled)
            widget.set_checked(channel.id in self._selected_ids)

            item.setSizeHint(widget.sizeHint())
            self._list_widget.setItemWidget(item, widget)
            self._item_widgets[channel.id] = widget

        # Update select-all button label
        if page_channels:
            total_visible = len(page_channels)
            selected_visible = sum(
                1 for ch in page_channels if ch.id in self._selected_ids
            )
            if selected_visible == total_visible:
                self._select_all_btn.setText("Снять выделение на странице")
            else:
                self._select_all_btn.setText("Выбрать всё на странице")
        else:
            self._select_all_btn.setText("Выбрать всё на странице")

    # ------------------------------------------------------------------
    # Slots
    # ------------------------------------------------------------------

    @Slot(str)
    def _on_search(self, query: str) -> None:
        """Re-filter when the search text changes."""
        self._page = 0
        self._filtered_channels = self._apply_filters()
        self._render_page()

    @Slot(int)
    def _on_filter_changed(self, index: int) -> None:
        """Re-filter when the filter combobox changes."""
        self._page = 0
        self._filtered_channels = self._apply_filters()
        self._render_page()

    @Slot(int)
    def _on_sort_changed(self, index: int) -> None:
        """Re-sort when the sort combobox changes."""
        self._filtered_channels = self._apply_filters()
        self._render_page()

    @Slot(int, bool)
    def _on_item_toggled(self, channel_id: int, checked: bool) -> None:
        """Handle a single channel checkbox toggle."""
        if checked:
            self._selected_ids.add(channel_id)
        else:
            self._selected_ids.discard(channel_id)

        self._update_selection_label()

        # Update select-all button text on current page
        self._update_select_all_button()

    @Slot()
    def _on_select_all_page(self) -> None:
        """Toggle selection of all items on the current page."""
        start = self._page * PAGE_SIZE
        end = start + PAGE_SIZE
        page_channels = self._filtered_channels[start:end]

        if not page_channels:
            return

        # Check: are all visible items already selected?
        all_selected = all(
            ch.id in self._selected_ids for ch in page_channels
        )

        if all_selected:
            # Deselect all visible
            for ch in page_channels:
                self._selected_ids.discard(ch.id)
                if ch.id in self._item_widgets:
                    self._item_widgets[ch.id].set_checked(False)
            self._select_all_btn.setText("Выбрать всё на странице")
        else:
            # Select all visible
            for ch in page_channels:
                self._selected_ids.add(ch.id)
                if ch.id in self._item_widgets:
                    self._item_widgets[ch.id].set_checked(True)
            self._select_all_btn.setText("Снять выделение на странице")

        self._update_selection_label()
        self.selection_changed.emit(len(self._selected_ids))

    def _on_scroll(self, value: int) -> None:
        """Detect scroll-to-bottom and load the next page."""
        scrollbar = self._list_widget.verticalScrollBar()
        if scrollbar.maximum() > 0 and value >= scrollbar.maximum() - 10:
            # Check if there are more pages
            total_filtered = len(self._filtered_channels)
            next_start = (self._page + 1) * PAGE_SIZE
            if next_start < total_filtered:
                self._page += 1
                self._render_page()
            elif self._on_load_more:
                self._on_load_more()

    # ------------------------------------------------------------------
    # Query helpers
    # ------------------------------------------------------------------

    def get_selected_channel_ids(self) -> list[int]:
        """Return the list of currently selected channel IDs."""
        return list(self._selected_ids)

    def get_selected_channels(self) -> list[Channel]:
        """Return the ``Channel`` objects for all selected IDs."""
        id_map = {ch.id: ch for ch in self._all_channels}
        return [id_map[cid] for cid in self._selected_ids if cid in id_map]

    def select_all(self) -> None:
        """Select every channel in the list."""
        for ch in self._all_channels:
            self._selected_ids.add(ch.id)
            if ch.id in self._item_widgets:
                self._item_widgets[ch.id].set_checked(True)
        self._update_selection_label()
        self.selection_changed.emit(len(self._selected_ids))

    def deselect_all(self) -> None:
        """Deselect every channel."""
        self._selected_ids.clear()
        for widget in self._item_widgets.values():
            widget.set_checked(False)
        self._update_selection_label()
        self.selection_changed.emit(0)

    def total_count(self) -> int:
        return len(self._all_channels)

    def filtered_count(self) -> int:
        return len(self._filtered_channels)

    # ------------------------------------------------------------------
    # Helpers
    # ------------------------------------------------------------------

    def _update_selection_label(self) -> None:
        count = len(self._selected_ids)
        self._selection_label.setText(
            f"Выбрано: {count} из {len(self._filtered_channels)}"
        )
        self.selection_changed.emit(count)

    def _update_select_all_button(self) -> None:
        start = self._page * PAGE_SIZE
        end = start + PAGE_SIZE
        page_channels = self._filtered_channels[start:end]
        if not page_channels:
            return
        all_selected = all(
            ch.id in self._selected_ids for ch in page_channels
        )
        self._select_all_btn.setText(
            "Снять выделение на странице" if all_selected else "Выбрать всё на странице"
        )
