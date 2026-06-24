"""Channel list widget for TeleClean.

Displays channels with checkboxes, search, filters, sorting
and a selection counter.  Uses QListView with a custom
QStyledItemDelegate for pixel-perfect rendering.
"""

from __future__ import annotations

import logging
from typing import Callable, Optional

from PySide6.QtCore import QModelIndex, QRect, QRectF, QSize, Qt, Signal, Slot
from PySide6.QtGui import (
    QColor,
    QFont,
    QPainter,
    QPainterPath,
    QPen,
    QPixmap,
)
from PySide6.QtWidgets import (
    QComboBox,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QListWidget,
    QListWidgetItem,
    QPushButton,
    QStyledItemDelegate,
    QStyleOptionViewItem,
    QVBoxLayout,
    QWidget,
)

from app.models.channel import Channel
from app.services.session_service import get_avatar_path

logger = logging.getLogger(__name__)

AVATAR_SIZE = 40
ROW_HEIGHT = 56
MARGIN_LR = 14
SPACING = 12
CHECKBOX_SIZE = 18


class ChannelDelegate(QStyledItemDelegate):
    """Paints a channel row with avatar, checkbox, text, and left badge."""

    def __init__(self, left_ids: set[int], parent=None) -> None:
        super().__init__(parent)
        self._left_ids = left_ids
        self._selected_ids: set[int] = set()
        self._channel_map: dict[int, Channel] = {}
        self._avatar_cache: dict[int, QPixmap] = {}

    def set_left_ids(self, left_ids: set[int]) -> None:
        self._left_ids = left_ids

    def set_selected_ids(self, ids: set[int]) -> None:
        self._selected_ids = ids

    def update_channel_map(self, channels: list[Channel]) -> None:
        self._channel_map = {ch.id: ch for ch in channels}

    def _get_avatar(self, channel_id: int, chan: Channel) -> QPixmap:
        if channel_id in self._avatar_cache:
            return self._avatar_cache[channel_id]
        path = get_avatar_path(channel_id)
        if path.exists():
            raw = QPixmap(str(path))
            if not raw.isNull():
                circ = QPixmap(AVATAR_SIZE, AVATAR_SIZE)
                circ.fill(Qt.transparent)
                p = QPainter(circ)
                p.setRenderHint(QPainter.Antialiasing)
                pth = QPainterPath()
                pth.addEllipse(0, 0, AVATAR_SIZE, AVATAR_SIZE)
                p.setClipPath(pth)
                src = raw.scaled(
                    AVATAR_SIZE, AVATAR_SIZE,
                    Qt.KeepAspectRatioByExpanding, Qt.SmoothTransformation,
                )
                off = (src.width() - AVATAR_SIZE) // 2
                p.drawPixmap(-off, 0, src)
                p.end()
                self._avatar_cache[channel_id] = circ
                return circ
        return QPixmap()

    def sizeHint(self, option, index) -> QSize:
        return QSize(0, ROW_HEIGHT)

    def paint(self, painter: QPainter, option: QStyleOptionViewItem, index: QModelIndex) -> None:
        from PySide6.QtWidgets import QStyle
        channel = index.data(Qt.UserRole)
        if not isinstance(channel, Channel):
            super().paint(painter, option, index)
            return

        painter.save()
        painter.setRenderHint(QPainter.Antialiasing)
        rect = option.rect
        w = rect.width()
        is_left = channel.id in self._left_ids
        is_selected = channel.id in self._selected_ids
        is_hover = bool(option.state & QStyle.StateFlag.State_MouseOver)

        # Background
        bg = QColor(42, 42, 42) if is_hover else QColor(36, 36, 36)
        painter.fillRect(rect, bg)

        x = rect.x() + MARGIN_LR
        cy = rect.y() + ROW_HEIGHT // 2  # vertical center

        # --- Avatar ---
        av = self._get_avatar(channel.id, channel)
        av_x = x
        av_y = cy - AVATAR_SIZE // 2
        if is_left:
            painter.setOpacity(0.4)
        if av.isNull():
            painter.setBrush(QColor(41, 144, 255) if not is_left else QColor(85, 85, 85))
            painter.setPen(Qt.NoPen)
            painter.drawEllipse(av_x, av_y, AVATAR_SIZE, AVATAR_SIZE)
            painter.setPen(QColor(255, 255, 255))
            font = QFont()
            font.setPixelSize(18)
            font.setBold(True)
            painter.setFont(font)
            painter.drawText(QRect(av_x, av_y, AVATAR_SIZE, AVATAR_SIZE),
                             Qt.AlignCenter, channel.title[0].upper() if channel.title else "?")
        else:
            painter.drawPixmap(av_x, av_y, av)
        painter.setOpacity(1.0)
        x += AVATAR_SIZE + SPACING

        # --- Checkbox ---
        cb_x = x
        cb_y = cy - CHECKBOX_SIZE // 2
        if is_left:
            pen_color = QColor(85, 85, 85)
        elif is_selected:
            painter.setBrush(QColor(41, 144, 255))
            pen_color = QColor(41, 144, 255)
        else:
            painter.setBrush(Qt.transparent)
            pen_color = QColor(120, 121, 126)
        pen = QPen(pen_color, 2)
        painter.setPen(pen)
        painter.drawRoundedRect(cb_x, cb_y, CHECKBOX_SIZE, CHECKBOX_SIZE, 4, 4)
        if is_selected:
            painter.setPen(QPen(QColor(255, 255, 255), 2))
            painter.drawLine(cb_x + 4, cb_y + CHECKBOX_SIZE // 2,
                             cb_x + 8, cb_y + CHECKBOX_SIZE - 4)
            painter.drawLine(cb_x + 8, cb_y + CHECKBOX_SIZE - 4,
                             cb_x + CHECKBOX_SIZE - 3, cb_y + 3)
        x += CHECKBOX_SIZE + SPACING

        # --- Text ---
        text_w = w - x - MARGIN_LR - 80  # leave room for badge
        if text_w < 20:
            painter.restore()
            return

        title_color = QColor(120, 121, 126) if is_left else QColor(255, 255, 255)
        sub_color = QColor(85, 85, 85) if is_left else QColor(170, 170, 170)

        font_title = QFont()
        font_title.setPixelSize(14)
        font_title.setBold(True)
        if is_left:
            font_title.setStrikeOut(True)
        painter.setFont(font_title)
        painter.setPen(title_color)
        title_rect = QRect(x, cy - 17, text_w, 18)
        elided = painter.fontMetrics().elidedText(channel.title, Qt.ElideRight, text_w)
        painter.drawText(title_rect, Qt.AlignLeft | Qt.AlignBottom, elided)

        parts = []
        if channel.participant_count > 0:
            parts.append(f"👤 {channel.participant_count:,}")
        if channel.unread_count > 0:
            parts.append(f"💬 {channel.unread_count}")
        if channel.username:
            parts.append(f"@{channel.username}")
        subtitle = " | ".join(parts) if parts else "Нет данных"

        font_sub = QFont()
        font_sub.setPixelSize(13)
        painter.setFont(font_sub)
        painter.setPen(sub_color)
        sub_rect = QRect(x, cy + 1, text_w, 16)
        elided_sub = painter.fontMetrics().elidedText(subtitle, Qt.ElideRight, text_w)
        painter.drawText(sub_rect, Qt.AlignLeft | Qt.AlignTop, elided_sub)

        # --- Left badge ---
        if is_left:
            badge_x = w - MARGIN_LR - 70
            font_badge = QFont()
            font_badge.setPixelSize(13)
            font_badge.setBold(True)
            painter.setFont(font_badge)
            painter.setPen(QColor(50, 229, 94))
            painter.drawText(QRect(badge_x, rect.y(), 70, ROW_HEIGHT),
                             Qt.AlignRight | Qt.AlignVCenter, "✅ Вышел")

        painter.restore()


class ChannelListWidget(QWidget):
    """Main channel list widget with search, filter, sort, and selection."""

    selection_changed = Signal(object)
    leave_requested = Signal(object)

    def __init__(self, parent: Optional[QWidget] = None) -> None:
        super().__init__(parent)

        self._all_channels: list[Channel] = []
        self._filtered_channels: list[Channel] = []
        self._selected_ids: set[int] = set()
        self._left_ids: set[int] = set()

        self._filter_callbacks: list[Callable[[list[Channel]], list[Channel]]] = []
        self._sort_callback: Optional[Callable[[list[Channel]], list[Channel]]] = None

        self._delegate = ChannelDelegate(self._left_ids)

        self._build_ui()

    def _build_ui(self) -> None:
        main_layout = QVBoxLayout(self)
        main_layout.setContentsMargins(0, 0, 0, 0)
        main_layout.setSpacing(6)

        search_bar = QHBoxLayout()
        search_bar.setContentsMargins(0, 0, 0, 0)
        self._search_input = QLineEdit()
        self._search_input.setPlaceholderText("🔍  Поиск каналов...")
        self._search_input.textChanged.connect(self._on_search)
        search_bar.addWidget(self._search_input)
        main_layout.addLayout(search_bar)

        filter_row = QHBoxLayout()
        filter_row.setSpacing(6)
        filter_row.setContentsMargins(0, 0, 0, 0)
        filter_label = QLabel("Фильтр:")
        filter_label.setStyleSheet("font-size: 12px; color: #AAAAAA; padding: 0px;")
        filter_row.addWidget(filter_label)
        self._filter_combo = QComboBox()
        self._filter_combo.addItems(["Все каналы", "Непрочитанные", "Мало подписчиков"])
        self._filter_combo.currentIndexChanged.connect(self._on_filter_changed)
        filter_row.addWidget(self._filter_combo)
        filter_row.addSpacing(8)
        sort_label = QLabel("Сорт:")
        sort_label.setStyleSheet("font-size: 12px; color: #AAAAAA; padding: 0px;")
        filter_row.addWidget(sort_label)
        self._sort_combo = QComboBox()
        self._sort_combo.addItems(["По названию", "По активности", "По подписчикам"])
        self._sort_combo.currentIndexChanged.connect(self._on_sort_changed)
        filter_row.addWidget(self._sort_combo)
        filter_row.addStretch()
        main_layout.addLayout(filter_row)

        info_bar = QHBoxLayout()
        self._selection_label = QLabel("Выбрано: 0")
        self._selection_label.setStyleSheet("font-size: 13px; color: #AAAAAA; padding: 4px 0px;")
        info_bar.addWidget(self._selection_label)
        info_bar.addStretch()
        self._select_all_btn = QPushButton("Выбрать все")
        self._select_all_btn.setObjectName("linkButton")
        self._select_all_btn.clicked.connect(self._on_select_all)
        info_bar.addWidget(self._select_all_btn)
        main_layout.addLayout(info_bar)

        self._list = QListWidget()
        self._list.setVerticalScrollMode(QListWidget.ScrollPerPixel)
        self._list.setSpacing(0)
        self._list.setMouseTracking(True)
        self._list.setItemDelegate(self._delegate)
        self._list.itemClicked.connect(self._on_item_clicked)
        self._list.setStyleSheet("QListWidget { padding-bottom: 32px; }")
        main_layout.addWidget(self._list, 1)

    def set_channels(self, channels: list[Channel]) -> None:
        self._all_channels = channels
        self._selected_ids.clear()
        self._left_ids.clear()
        self._filtered_channels = self._apply_filters()
        self._delegate.update_channel_map(channels)
        self._rebuild()
        self.selection_changed.emit(0)

    def set_filter_callback(self, cb: Callable) -> None:
        self._filter_callbacks.append(cb)

    def set_sort_callback(self, cb: Callable) -> None:
        self._sort_callback = cb

    def mark_channels_left(self, channel_ids: list[int]) -> None:
        for cid in channel_ids:
            self._left_ids.add(cid)
            self._selected_ids.discard(cid)
        self._delegate.set_left_ids(self._left_ids)
        self._delegate.set_selected_ids(self._selected_ids)
        self._list.viewport().update()
        self._update_label()

    def _apply_filters(self) -> list[Channel]:
        result = self._all_channels[:]
        query = self._search_input.text().strip().lower()
        if query:
            result = [ch for ch in result if query in ch.title.lower()]
        idx = self._filter_combo.currentIndex()
        if idx == 1:
            result = [ch for ch in result if ch.unread_count > 0]
        elif idx == 2:
            result.sort(key=lambda c: c.participant_count)
            return result
        for cb in self._filter_callbacks:
            result = cb(result)
        sidx = self._sort_combo.currentIndex()
        if sidx == 0:
            result.sort(key=lambda c: c.title.lower())
        elif sidx == 1:
            result.sort(key=lambda c: (c.unread_count, c.title.lower()), reverse=True)
        elif sidx == 2:
            result.sort(key=lambda c: c.participant_count, reverse=True)
        if self._sort_callback:
            result = self._sort_callback(result)
        return result

    def _rebuild(self) -> None:
        self._list.clear()
        for ch in self._filtered_channels:
            item = QListWidgetItem(self._list)
            item.setData(Qt.UserRole, ch)
            item.setSizeHint(QSize(0, ROW_HEIGHT))
        self._update_label()

    def _on_item_clicked(self, item: QListWidgetItem) -> None:
        ch = item.data(Qt.UserRole)
        if not isinstance(ch, Channel) or ch.id in self._left_ids:
            return
        if ch.id in self._selected_ids:
            self._selected_ids.discard(ch.id)
        else:
            self._selected_ids.add(ch.id)
        self._delegate.set_selected_ids(self._selected_ids)
        self._list.viewport().update()
        self._update_label()

    @Slot(str)
    def _on_search(self, query: str) -> None:
        self._filtered_channels = self._apply_filters()
        self._rebuild()

    @Slot(int)
    def _on_filter_changed(self, idx: int) -> None:
        self._filtered_channels = self._apply_filters()
        self._rebuild()

    @Slot(int)
    def _on_sort_changed(self, idx: int) -> None:
        self._filtered_channels = self._apply_filters()
        self._rebuild()

    @Slot()
    def _on_select_all(self) -> None:
        if self._selected_ids:
            self.deselect_all()
        else:
            self.select_all()

    def select_all(self) -> None:
        for ch in self._all_channels:
            if ch.id not in self._left_ids:
                self._selected_ids.add(ch.id)
        self._delegate.set_selected_ids(self._selected_ids)
        self._list.viewport().update()
        self._update_label()

    def deselect_all(self) -> None:
        self._selected_ids.clear()
        self._delegate.set_selected_ids(self._selected_ids)
        self._list.viewport().update()
        self._update_label()

    def get_selected_channel_ids(self) -> list[int]:
        return list(self._selected_ids)

    def get_selected_channels(self) -> list[Channel]:
        id_map = {ch.id: ch for ch in self._all_channels}
        return [id_map[cid] for cid in self._selected_ids if cid in id_map]

    def total_count(self) -> int:
        return len(self._all_channels)

    def filtered_count(self) -> int:
        return len(self._filtered_channels)

    def left_count(self) -> int:
        return len(self._left_ids)

    def _update_label(self) -> None:
        count = len(self._selected_ids)
        total = len(self._filtered_channels)
        left = len(self._left_ids)
        label = f"Выбрано: {count} из {total}"
        if left:
            label += f"  |  Вышло: {left}"
        self._selection_label.setText(label)
        self._select_all_btn.setText("Снять выделение" if count > 0 else "Выбрать все")
        self.selection_changed.emit(count)
